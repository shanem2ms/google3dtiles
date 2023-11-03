#define WIN32_LEAN_AND_MEAN             // Exclude rarely-used stuff from Windows headers
#define NOMINMAX
#define WIN32_LEAN_AND_MEAN             // Exclude rarely-used stuff from Windows headers
// Windows Header Files
#include <chrono>
#include <stdio.h>
#include <string>
#include <vector>
#include <crtdbg.h>
#include <iostream>


#define TINYGLTF_IMPLEMENTATION
#define STB_IMAGE_IMPLEMENTATION
#define STB_IMAGE_WRITE_IMPLEMENTATION
#include "tiny_gltf.h"


void SetupMeshState(tinygltf::Model& model);

int main(int argc, char *argv[])
{
    if (argc < 2)
        return -1;
    tinygltf::Model model;
    tinygltf::TinyGLTF loader;
    std::string err;
    std::string warn;
    loader.LoadBinaryFromFile(&model, &err, &warn, argv[1]);
    SetupMeshState(model);
}


static size_t ComponentTypeByteSize(int type) {
    switch (type) {
    case TINYGLTF_COMPONENT_TYPE_UNSIGNED_BYTE:
    case TINYGLTF_COMPONENT_TYPE_BYTE:
        return sizeof(char);
    case TINYGLTF_COMPONENT_TYPE_UNSIGNED_SHORT:
    case TINYGLTF_COMPONENT_TYPE_SHORT:
        return sizeof(short);
    case TINYGLTF_COMPONENT_TYPE_UNSIGNED_INT:
    case TINYGLTF_COMPONENT_TYPE_INT:
        return sizeof(int);
    case TINYGLTF_COMPONENT_TYPE_FLOAT:
        return sizeof(float);
    case TINYGLTF_COMPONENT_TYPE_DOUBLE:
        return sizeof(double);
    default:
        return 0;
    }
}

void SetupMeshState(tinygltf::Model& model) {
    // Buffer
    {
        for (size_t i = 0; i < model.bufferViews.size(); i++) {
            const tinygltf::BufferView& bufferView = model.bufferViews[i];
            if (bufferView.target == 0) {
                std::cout << "WARN: bufferView.target is zero" << std::endl;
                //continue;  // Unsupported bufferView.
            }

            int sparse_accessor = -1;
            for (size_t a_i = 0; a_i < model.accessors.size(); ++a_i) {
                const auto& accessor = model.accessors[a_i];
                if (accessor.bufferView == i) {
                    std::cout << i << " is used by accessor " << a_i << std::endl;
                    if (accessor.sparse.isSparse) {
                        std::cout
                            << "WARN: this bufferView has at least one sparse accessor to "
                            "it. We are going to load the data as patched by this "
                            "sparse accessor, not the original data"
                            << std::endl;
                        sparse_accessor = a_i;
                        break;
                    }
                }
            }

            const tinygltf::Buffer& buffer = model.buffers[bufferView.buffer];
            
            //GLBufferState state;
            //glGenBuffers(1, &state.vb);
            //glBindBuffer(bufferView.target, state.vb);
            std::cout << "buffer.size= " << buffer.data.size()
                << ", byteOffset = " << bufferView.byteOffset << std::endl;

            if (sparse_accessor < 0)
                std::cout <<"sparse_accessor < 0";
            else {
                const auto accessor = model.accessors[sparse_accessor];
                // copy the buffer to a temporary one for sparse patching
                unsigned char* tmp_buffer = new unsigned char[bufferView.byteLength];
                memcpy(tmp_buffer, buffer.data.data() + bufferView.byteOffset,
                    bufferView.byteLength);

                const size_t size_of_object_in_buffer =
                    ComponentTypeByteSize(accessor.componentType);
                const size_t size_of_sparse_indices =
                    ComponentTypeByteSize(accessor.sparse.indices.componentType);

                const auto& indices_buffer_view =
                    model.bufferViews[accessor.sparse.indices.bufferView];
                const auto& indices_buffer = model.buffers[indices_buffer_view.buffer];

                const auto& values_buffer_view =
                    model.bufferViews[accessor.sparse.values.bufferView];
                const auto& values_buffer = model.buffers[values_buffer_view.buffer];

                for (size_t sparse_index = 0; sparse_index < accessor.sparse.count;
                    ++sparse_index) {
                    int index = 0;
                    // std::cout << "accessor.sparse.indices.componentType = " <<
                    // accessor.sparse.indices.componentType << std::endl;
                    switch (accessor.sparse.indices.componentType) {
                    case TINYGLTF_COMPONENT_TYPE_BYTE:
                    case TINYGLTF_COMPONENT_TYPE_UNSIGNED_BYTE:
                        index = (int)*(
                            unsigned char*)(indices_buffer.data.data() +
                                indices_buffer_view.byteOffset +
                                accessor.sparse.indices.byteOffset +
                                (sparse_index * size_of_sparse_indices));
                        break;
                    case TINYGLTF_COMPONENT_TYPE_SHORT:
                    case TINYGLTF_COMPONENT_TYPE_UNSIGNED_SHORT:
                        index = (int)*(
                            unsigned short*)(indices_buffer.data.data() +
                                indices_buffer_view.byteOffset +
                                accessor.sparse.indices.byteOffset +
                                (sparse_index * size_of_sparse_indices));
                        break;
                    case TINYGLTF_COMPONENT_TYPE_INT:
                    case TINYGLTF_COMPONENT_TYPE_UNSIGNED_INT:
                        index = (int)*(
                            unsigned int*)(indices_buffer.data.data() +
                                indices_buffer_view.byteOffset +
                                accessor.sparse.indices.byteOffset +
                                (sparse_index * size_of_sparse_indices));
                        break;
                    }
                    std::cout << "updating sparse data at index  : " << index
                        << std::endl;
                    // index is now the target of the sparse index to patch in
                    const unsigned char* read_from =
                        values_buffer.data.data() +
                        (values_buffer_view.byteOffset +
                            accessor.sparse.values.byteOffset) +
                        (sparse_index * (size_of_object_in_buffer * accessor.type));

                    /*
                    std::cout << ((float*)read_from)[0] << "\n";
                    std::cout << ((float*)read_from)[1] << "\n";
                    std::cout << ((float*)read_from)[2] << "\n";
                    */

                    unsigned char* write_to =
                        tmp_buffer + index * (size_of_object_in_buffer * accessor.type);

                    memcpy(write_to, read_from, size_of_object_in_buffer * accessor.type);
                }

                // debug:
                /*for(size_t p = 0; p < bufferView.byteLength/sizeof(float); p++)
                {
                  float* b = (float*)tmp_buffer;
                  std::cout << "modified_buffer [" << p << "] = " << b[p] << '\n';
                }*/

                delete[] tmp_buffer;
            }
        }
    }

#if 0  // TODO(syoyo): Implement
    // Texture
    {
        for (size_t i = 0; i < model.meshes.size(); i++) {
            const tinygltf::Mesh& mesh = model.meshes[i];

            gMeshState[mesh.name].diffuseTex.resize(mesh.primitives.size());
            for (size_t primId = 0; primId < mesh.primitives.size(); primId++) {
                const tinygltf::Primitive& primitive = mesh.primitives[primId];

                gMeshState[mesh.name].diffuseTex[primId] = 0;

                if (primitive.material < 0) {
                    continue;
                }
                tinygltf::Material& mat = model.materials[primitive.material];
                // printf("material.name = %s\n", mat.name.c_str());
                if (mat.values.find("diffuse") != mat.values.end()) {
                    std::string diffuseTexName = mat.values["diffuse"].string_value;
                    if (model.textures.find(diffuseTexName) != model.textures.end()) {
                        tinygltf::Texture& tex = model.textures[diffuseTexName];
                        if (scene.images.find(tex.source) != model.images.end()) {
                            tinygltf::Image& image = model.images[tex.source];
                            GLuint texId;
                            glGenTextures(1, &texId);
                            glBindTexture(tex.target, texId);
                            glPixelStorei(GL_UNPACK_ALIGNMENT, 1);
                            glTexParameterf(tex.target, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
                            glTexParameterf(tex.target, GL_TEXTURE_MAG_FILTER, GL_LINEAR);

                            // Ignore Texture.fomat.
                            GLenum format = GL_RGBA;
                            if (image.component == 3) {
                                format = GL_RGB;
                            }
                            glTexImage2D(tex.target, 0, tex.internalFormat, image.width,
                                image.height, 0, format, tex.type,
                                &image.image.at(0));

                            CheckErrors("texImage2D");
                            glBindTexture(tex.target, 0);

                            printf("TexId = %d\n", texId);
                            gMeshState[mesh.name].diffuseTex[primId] = texId;
                        }
                    }
                }
            }
        }
    }
#endif

}
