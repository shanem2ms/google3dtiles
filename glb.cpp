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
#include "draco/compression/decode.h"


struct pt3
{
    float x;
    float y;
    float z;
    float u;
    float v;
};

tinygltf::TinyGLTF loader;

struct GltfModel
{
    tinygltf::Model model;
    draco::Mesh mesh;
};

extern "C" _declspec(dllexport) GltfModel *LoadMesh(const uint8_t *srcMem, const uint32_t size)
{
    GltfModel* pmodel = new GltfModel();
    tinygltf::Model &model = pmodel->model;
    std::string err;
    std::string warn;
    bool success = loader.LoadBinaryFromMemory(&model, &err, &warn, srcMem, size);
    if (!success)
        return nullptr;
    draco::DecoderBuffer buffer;
    buffer.Init(
        (const char*)model.buffers[0].data.data(), model.buffers[0].data.size());
    draco::Decoder decoder;
    auto result = decoder.DecodeBufferToGeometry(&buffer, &pmodel->mesh);
    if (result.ok())
        return pmodel;
    else
        return nullptr;
}


extern "C" _declspec(dllexport) uint32_t FaceCount(GltfModel *pmodel)
{
    return pmodel->mesh.num_faces();
}

extern "C" _declspec(dllexport) bool GetFaces(GltfModel *pmodel, void* pFaces, uint32_t bufSize)
{
    std::vector<uint32_t> triangleList;
    for (uint32_t faceIdx = 0; faceIdx < pmodel->mesh.num_faces(); ++faceIdx)
    {
        const draco::Mesh::Face &face = pmodel->mesh.face(draco::FaceIndex(faceIdx));
        triangleList.push_back(face[0].value());
        triangleList.push_back(face[1].value());
        triangleList.push_back(face[2].value());
    }
    if (triangleList.size() * sizeof(uint32_t) > bufSize)
        return false;
    memcpy(pFaces, triangleList.data(), triangleList.size() * sizeof(uint32_t));
    return true;
}

extern "C" _declspec(dllexport) uint32_t GetTextureWidth(GltfModel * pmodel)
{
    return pmodel->model.images[0].width;
}

extern "C" _declspec(dllexport) uint32_t GetTextureHeight(GltfModel * pmodel)
{
    return pmodel->model.images[0].height;
}

extern "C" _declspec(dllexport) bool GetTexture(GltfModel * pmodel, void* pTexture, uint32_t bufSize)
{
    std::vector<uint8_t> &img = pmodel->model.images[0].image;
    if (img.size() > bufSize)
        return false;
    memcpy(pTexture, img.data(), img.size());
    return true;
}

extern "C" _declspec(dllexport) uint32_t PtCount(GltfModel *pmodel)
{
    return pmodel->mesh.num_points();
}

extern "C" _declspec(dllexport) bool GetPoints(GltfModel *pmodel, void* ptbuf, uint32_t bufSize, 
    float *pmatrix)
{
    for (int i = 0; i < 16; ++i)
    {
        pmatrix[i] = pmodel->model.nodes[0].matrix[i];
    }

    draco::Mesh &mesh = pmodel->mesh;
    const draco::PointAttribute* posAttribute = mesh.GetNamedAttribute(draco::GeometryAttribute::Type::POSITION);
    const draco::PointAttribute* uvAttribute = mesh.GetNamedAttribute(draco::GeometryAttribute::Type::TEX_COORD);
    uint32_t numPts = mesh.num_points();
    std::vector<pt3> points;
    for (uint32_t ptIdx = 0; ptIdx < numPts; ++ptIdx)
    {
        pt3 pt;
        posAttribute->GetMappedValue(draco::PointIndex(ptIdx), &pt);
        uvAttribute->GetMappedValue(draco::PointIndex(ptIdx), &pt.u);
        points.push_back(pt);
    }
    if (points.size() * sizeof(pt3) > bufSize)
        return false;
    memcpy(ptbuf, points.data(), points.size() * sizeof(pt3));
    return true;
}

extern "C" _declspec(dllexport) void FreeMesh(GltfModel *pmodel)
{
    delete pmodel;
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


int main(int argc, char* argv[])
{
    if (argc < 2)
        return -1;
    tinygltf::Model model;
    tinygltf::TinyGLTF loader;
    std::string err;
    std::string warn;
    loader.LoadBinaryFromFile(&model, &err, &warn, argv[1]);    
    draco::DecoderBuffer buffer;
    buffer.Init(
        (const char*)model.buffers[0].data.data(), model.buffers[0].data.size());
    draco::Decoder decoder;
    draco::Mesh mesh;
    auto result = decoder.DecodeBufferToGeometry(&buffer, &mesh);
}