using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;
using System.Text;
using Veldrid;
using Veldrid.SPIRV;
using System.Windows;
using System.Windows.Input;
using System.Diagnostics;

namespace googletiles
{
    public class EarthViz
    {
        private readonly VertexPositionTexture[] _vertices;
        private readonly uint[] _indices;
        private DeviceBuffer _projectionBuffer;
        private DeviceBuffer _viewBuffer;
        private DeviceBuffer _vertexBuffer;
        private DeviceBuffer _indexBuffer;
        private Pipeline _pipeline;
        private ResourceSet _projViewSet;
        private float _ticks;

        // 8000000
        Tile root;
        public EarthViz(Tile rootTile)
        {
            root = rootTile;
            _vertices = GetCubeVertices();
            _indices = GetCubeIndices();
        }

        public void CreateResources(GraphicsDevice gd, Swapchain sc, ResourceFactory factory)
        {
            _projectionBuffer = factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));
            _viewBuffer = factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));

            _vertexBuffer = factory.CreateBuffer(new BufferDescription((uint)(VertexPositionTexture.SizeInBytes * _vertices.Length), BufferUsage.VertexBuffer));
            gd.UpdateBuffer(_vertexBuffer, 0, _vertices);

            _indexBuffer = factory.CreateBuffer(new BufferDescription(sizeof(uint) * (uint)_indices.Length, BufferUsage.IndexBuffer));
            gd.UpdateBuffer(_indexBuffer, 0, _indices);


            ShaderSetDescription shaderSet = new ShaderSetDescription(
                new[]
                {
                    new VertexLayoutDescription(
                        new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                        new VertexElementDescription("TexCoords", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2))
                },
                factory.CreateFromSpirv(
                    new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(VertexCode), "main"),
                    new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(FragmentCode), "main")));

            ResourceLayout projViewLayout = factory.CreateResourceLayout(
                new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("ProjectionBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                    new ResourceLayoutElementDescription("ViewBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

            ResourceLayout worldTextureLayout = factory.CreateResourceLayout(
                new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("WorldBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex), 
                    new ResourceLayoutElementDescription("SurfaceTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                    new ResourceLayoutElementDescription("SurfaceSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

            RasterizerStateDescription description = new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Wireframe, FrontFace.Clockwise, true, false);
            _pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                BlendStateDescription.SingleOverrideBlend,
                DepthStencilStateDescription.DepthOnlyLessEqual,
                description,
                PrimitiveTopology.TriangleList,
                shaderSet,
                new[] { projViewLayout, worldTextureLayout },
                sc.Framebuffer.OutputDescription));

            _projViewSet = factory.CreateResourceSet(new ResourceSetDescription(
                projViewLayout,
                _projectionBuffer,
                _viewBuffer));

        }

        Point mouseDownPt;
        bool mouseDown = false;
        Quaternion camRot = Quaternion.Identity;
        Quaternion camRotMouseDown;
        Vector3 camPos = new Vector3(0, 0, 2);
        Vector3 wsMotion;
        Vector3 adMotion;
        Vector3 eqMotion;
        public void OnMouseDown(MouseButtonEventArgs e, Point point)
        {
            mouseDownPt = point;
            camRotMouseDown = camRot;
            mouseDown = true;
        }

        float rotSpeed = 0.005f;
        public void OnMouseMove(MouseEventArgs e, Point point)
        {
            if (mouseDown)
            {
                double xdiff = point.X - mouseDownPt.X;
                double ydiff = point.Y - mouseDownPt.Y;
                camRot = camRotMouseDown * Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)-xdiff * rotSpeed) *
                    Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)-ydiff * rotSpeed);
            }
        }
        public void OnMouseUp(MouseButtonEventArgs e, Point point)
        {
            mouseDown = false;
        }

        float speed = 0.01f;
        public void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.W)
            {
                Vector3 dir = Vector3.Transform(Vector3.UnitZ, camRot);
                wsMotion = -dir * speed;
            }
            else if (e.Key == Key.S)
            {
                Vector3 dir = Vector3.Transform(Vector3.UnitZ, camRot);
                wsMotion = dir * speed;
            }
            else if (e.Key == Key.A)
            {
                Vector3 dir = Vector3.Transform(Vector3.UnitX, camRot);
                adMotion = -dir * speed;
            }
            else if (e.Key == Key.D)
            {
                Vector3 dir = Vector3.Transform(Vector3.UnitX, camRot);
                adMotion = dir * speed;
            }
            else if (e.Key == Key.E)
            {
                Vector3 dir = Vector3.Transform(Vector3.UnitY, camRot);
                eqMotion = dir * speed;
            }
            else if (e.Key == Key.Q)
            {
                Vector3 dir = Vector3.Transform(Vector3.UnitY, camRot);
                eqMotion = -dir * speed;
            }
        }
        public void OnKeyUp(KeyEventArgs e)
        {
            if (e.Key == Key.W || e.Key == Key.S)
            {
                wsMotion = Vector3.Zero;
            }
            else if (e.Key == Key.A || e.Key == Key.D)
            {
                adMotion = Vector3.Zero;
            }
            else if (e.Key == Key.E || e.Key == Key.Q)
            {
                eqMotion = Vector3.Zero;
            }

        }
        void DrawTile(CommandList cl, Tile tile)
        {
            if (tile == null)
                return;

            if (tile.mesh != null)
            {
                cl.UpdateBuffer(_projectionBuffer, 0, Matrix4x4.CreatePerspectiveFieldOfView(
                    1.0f,
                    1.0f,
                    0.05f,
                    10f));

                Matrix4x4 viewMat =
                    Matrix4x4.CreateFromQuaternion(camRot) *
                    Matrix4x4.CreateTranslation(camPos);
                Matrix4x4.Invert(viewMat, out viewMat);

                cl.UpdateBuffer(_viewBuffer, 0, ref viewMat);
                cl.SetVertexBuffer(0, tile.mesh._vertexBuffer);
                cl.SetIndexBuffer(tile.mesh._indexBuffer, IndexFormat.UInt32);
                cl.SetGraphicsResourceSet(0, _projViewSet);
                cl.SetGraphicsResourceSet(1, tile.mesh._worldTextureSet);
                cl.DrawIndexed((uint)tile.mesh.triangleCnt, 1, 0, 0, 0);
            }
            if (tile.ChildTiles != null)
            {
                foreach (Tile childTile in tile.ChildTiles)
                { DrawTile(cl, childTile); }
            }
        }


        public void Draw(CommandList cl, float deltaSeconds)
        {

            camPos += wsMotion;
            camPos += adMotion;
            camPos += eqMotion;
            cl.ClearColorTarget(0, RgbaFloat.Black);
            cl.ClearDepthStencil(1f);
            cl.SetPipeline(_pipeline);
            DrawTile(cl, root);            
        }

        private static VertexPositionTexture[] GetCubeVertices()
        {
            VertexPositionTexture[] vertices = new VertexPositionTexture[]
            {
                // Top
                new VertexPositionTexture(new Vector3(-0.5f, +0.5f, -0.5f), new Vector2(0, 0)),
                new VertexPositionTexture(new Vector3(+0.5f, +0.5f, -0.5f), new Vector2(1, 0)),
                new VertexPositionTexture(new Vector3(+0.5f, +0.5f, +0.5f), new Vector2(1, 1)),
                new VertexPositionTexture(new Vector3(-0.5f, +0.5f, +0.5f), new Vector2(0, 1)),
                // Bottom                                                             
                new VertexPositionTexture(new Vector3(-0.5f,-0.5f, +0.5f),  new Vector2(0, 0)),
                new VertexPositionTexture(new Vector3(+0.5f,-0.5f, +0.5f),  new Vector2(1, 0)),
                new VertexPositionTexture(new Vector3(+0.5f,-0.5f, -0.5f),  new Vector2(1, 1)),
                new VertexPositionTexture(new Vector3(-0.5f,-0.5f, -0.5f),  new Vector2(0, 1)),
                // Left                                                               
                new VertexPositionTexture(new Vector3(-0.5f, +0.5f, -0.5f), new Vector2(0, 0)),
                new VertexPositionTexture(new Vector3(-0.5f, +0.5f, +0.5f), new Vector2(1, 0)),
                new VertexPositionTexture(new Vector3(-0.5f, -0.5f, +0.5f), new Vector2(1, 1)),
                new VertexPositionTexture(new Vector3(-0.5f, -0.5f, -0.5f), new Vector2(0, 1)),
                // Right                                                              
                new VertexPositionTexture(new Vector3(+0.5f, +0.5f, +0.5f), new Vector2(0, 0)),
                new VertexPositionTexture(new Vector3(+0.5f, +0.5f, -0.5f), new Vector2(1, 0)),
                new VertexPositionTexture(new Vector3(+0.5f, -0.5f, -0.5f), new Vector2(1, 1)),
                new VertexPositionTexture(new Vector3(+0.5f, -0.5f, +0.5f), new Vector2(0, 1)),
                // Back                                                               
                new VertexPositionTexture(new Vector3(+0.5f, +0.5f, -0.5f), new Vector2(0, 0)),
                new VertexPositionTexture(new Vector3(-0.5f, +0.5f, -0.5f), new Vector2(1, 0)),
                new VertexPositionTexture(new Vector3(-0.5f, -0.5f, -0.5f), new Vector2(1, 1)),
                new VertexPositionTexture(new Vector3(+0.5f, -0.5f, -0.5f), new Vector2(0, 1)),
                // Front                                                              
                new VertexPositionTexture(new Vector3(-0.5f, +0.5f, +0.5f), new Vector2(0, 0)),
                new VertexPositionTexture(new Vector3(+0.5f, +0.5f, +0.5f), new Vector2(1, 0)),
                new VertexPositionTexture(new Vector3(+0.5f, -0.5f, +0.5f), new Vector2(1, 1)),
                new VertexPositionTexture(new Vector3(-0.5f, -0.5f, +0.5f), new Vector2(0, 1)),
            };

            return vertices;
        }

        private static uint[] GetCubeIndices()
        {
            uint[] indices =
            {
                0,1,2, 0,2,3,
                4,5,6, 4,6,7,
                8,9,10, 8,10,11,
                12,13,14, 12,14,15,
                16,17,18, 16,18,19,
                20,21,22, 20,22,23,
            };

            return indices;
        }

        private const string VertexCode = @"
#version 450

layout(set = 0, binding = 0) uniform ProjectionBuffer
{
    mat4 Projection;
};

layout(set = 0, binding = 1) uniform ViewBuffer
{
    mat4 View;
};

layout(set = 1, binding = 0) uniform WorldBuffer
{
    mat4 World;
};

layout(location = 0) in vec3 Position;
layout(location = 1) in vec2 TexCoords;
layout(location = 0) out vec2 fsin_texCoords;

void main()
{
    vec4 worldPosition = World * vec4(Position, 1);
    vec4 viewPosition = View * worldPosition;
    vec4 clipPosition = Projection * viewPosition;
    gl_Position = clipPosition;
    fsin_texCoords = TexCoords;
}";

        private const string FragmentCode = @"
#version 450

layout(location = 0) in vec2 fsin_texCoords;
layout(location = 0) out vec4 fsout_color;

layout(set = 1, binding = 1) uniform texture2D SurfaceTexture;
layout(set = 1, binding = 2) uniform sampler SurfaceSampler;

void main()
{
    fsout_color =  texture(sampler2D(SurfaceTexture, SurfaceSampler), fsin_texCoords);
}";
    }

    public struct VertexPositionTexture
    {
        public const uint SizeInBytes = 20;

        public float PosX;
        public float PosY;
        public float PosZ;

        public float TexU;
        public float TexV;

        public VertexPositionTexture(Vector3 pos, Vector2 uv)
        {
            PosX = pos.X;
            PosY = pos.Y;
            PosZ = pos.Z;
            TexU = uv.X;
            TexV = uv.Y;
        }
    }
}