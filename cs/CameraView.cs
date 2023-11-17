using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows;

namespace googletiles
{
    public class CameraView
    {
        Point mouseDownPt;
        bool mouseDown = false;
        Vector3 camPos = new Vector3(1531617.9f, -4465250f, 4275288.5f);
        Quaternion camRot = new Quaternion(0.7424914f, 0.14525016f, 0.005905729f, 0.65389216f);

        Quaternion camRotMouseDown;
        static float scale = 8000000.0f;
        Vector3 wsMotion;
        Vector3 adMotion;
        Vector3 eqMotion;
        Quaternion dbgCamRot = Quaternion.Identity;
        Vector3 dbgCamPos = new Vector3(0, 0, scale);
        public bool DebugMode = false;
        bool dbgInput = false;
        Matrix4x4 viewProj = Matrix4x4.Identity;
        public Matrix4x4 ViewProj => viewProj;

        public Quaternion ViewRot => camRot;
        Quad[] frustumQuads = new Quad[6];
        Quaternion CamRot { get => dbgInput ? dbgCamRot : camRot; set { if (dbgInput) dbgCamRot = value; else camRot = value; } }
        Vector3 CamPos { get => dbgInput ? dbgCamPos : camPos; set { if (dbgInput) dbgCamPos = value; else camPos = value; } }


        public Quad[] FrustumQuads => frustumQuads;
        public CameraView()
        {
            Update();
        }

        public void OnMouseDown(MouseButtonEventArgs e, Point point)
        {
            mouseDownPt = point;
            camRotMouseDown = CamRot;
            mouseDown = true;
        }

        float rotSpeed = 0.005f;
        public void OnMouseMove(MouseEventArgs e, Point point)
        {
            if (mouseDown)
            {
                double xdiff = point.X - mouseDownPt.X;
                double ydiff = point.Y - mouseDownPt.Y;
                CamRot = camRotMouseDown * Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)-xdiff * rotSpeed) *
                    Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)-ydiff * rotSpeed);
            }
        }
        public void OnMouseUp(MouseButtonEventArgs e, Point point)
        {
            mouseDown = false;
        }

        void BuildFrustumQuads()
        {
            Matrix4x4 viewprojinv;
            Matrix4x4.Invert(viewProj, out viewprojinv);

            viewprojinv = Matrix4x4.CreateScale(2, 2, 1) * Matrix4x4.CreateTranslation(0, 0, 0.5f) *
                viewprojinv;

            for (int quadIdx = 0; quadIdx < frustumQuads.Length; ++quadIdx)
            {
                Vector3[] qpts = new Vector3[4];
                for (int i = 0; i < 4; ++i)
                {
                    Vector4 ppt = Vector4.Transform(quadPts[quadIdx * 4 + i], viewprojinv);
                    ppt /= ppt.W;
                    qpts[i] = new Vector3(ppt.X, ppt.Y, ppt.Z);
                }
                frustumQuads[quadIdx] = new Quad(qpts);
            }
        }

        float speed = 0.01f * scale;
        float Speed => (dbgInput ? scale : LookAtDist) * 0.01f;
        public void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.W)
            {
                Vector3 dir = Vector3.Transform(Vector3.UnitZ, CamRot);
                wsMotion = -dir * Speed;
            }
            else if (e.Key == Key.S)
            {
                Vector3 dir = Vector3.Transform(Vector3.UnitZ, CamRot);
                wsMotion = dir * Speed;
            }
            else if (e.Key == Key.A)
            {
                Vector3 dir = Vector3.Transform(Vector3.UnitX, CamRot);
                adMotion = -dir * Speed;
            }
            else if (e.Key == Key.D)
            {
                Vector3 dir = Vector3.Transform(Vector3.UnitX, CamRot);
                adMotion = dir * Speed;
            }
            else if (e.Key == Key.E)
            {
                Vector3 dir = Vector3.Transform(Vector3.UnitY, CamRot);
                eqMotion = dir * Speed;
            }
            else if (e.Key == Key.Q)
            {
                Vector3 dir = Vector3.Transform(Vector3.UnitY, CamRot);
                eqMotion = -dir * Speed;
            }
            else if (e.Key == Key.G)
            {
                DebugMode = !DebugMode;
            }
            else if (e.Key == Key.F)
            {
                dbgInput = !dbgInput;
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

        public float LookAtDist { get; set; } = float.PositiveInfinity;
        public Vector3 LookDir => -Vector3.Transform(Vector3.UnitZ, camRot);
        public Vector3 Pos => camPos;

        public void Update()
        {
            CamPos += wsMotion;
            CamPos += adMotion;
            CamPos += eqMotion;
            this.viewProj = this.ViewMat * this.ProjMat;
            BuildFrustumQuads();
        }

        public Matrix4x4 DbgProjMat
        {
            get
            {
                return Matrix4x4.CreatePerspectiveFieldOfView(
                    1.0f,
                    1.0f,
                    0.05f * scale,
                    10f * scale);
            }
        }
        public Matrix4x4 DbgViewMat
        {
            get
            {
                Matrix4x4 viewMat =
                        Matrix4x4.CreateFromQuaternion(dbgCamRot) *
                        Matrix4x4.CreateTranslation(dbgCamPos);
                Matrix4x4.Invert(viewMat, out viewMat);
                return viewMat;
            }
        }

        public Matrix4x4 ProjMat
        {
            get
            {
                return Matrix4x4.CreatePerspectiveFieldOfView(
                    1.0f,
                    1.0f,
                    0.05f * Math.Min(LookAtDist, scale),
                    10f * Math.Min(LookAtDist, scale));
            }
        }
        public Matrix4x4 ViewMat
        {
            get
            {
                Matrix4x4 viewMat =
                        Matrix4x4.CreateFromQuaternion(camRot) *
                        Matrix4x4.CreateTranslation(camPos);
                Matrix4x4.Invert(viewMat, out viewMat);
                return viewMat;
            }
        }
        public Matrix4x4 ViewMatNoTranslate
        {
            get
            {
                Matrix4x4 viewMat =
                        Matrix4x4.CreateFromQuaternion(camRot);
                Matrix4x4.Invert(viewMat, out viewMat);
                return viewMat;
            }
        }

        static Vector3[] quadPts = {
                // Top
                new Vector3(-0.5f, +0.5f, -0.5f),
                new Vector3(+0.5f, +0.5f, -0.5f),
                new Vector3(+0.5f, +0.5f, +0.5f),
                new Vector3(-0.5f, +0.5f, +0.5f),
                // Bottom                                                             
                new Vector3(-0.5f, -0.5f, +0.5f),
                new Vector3(+0.5f, -0.5f, +0.5f),
                new Vector3(+0.5f, -0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f, -0.5f),
                // Left                                                               
                new Vector3(-0.5f, +0.5f, -0.5f),
                new Vector3(-0.5f, +0.5f, +0.5f),
                new Vector3(-0.5f, -0.5f, +0.5f),
                new Vector3(-0.5f, -0.5f, -0.5f),
                // Right                                                              
                new Vector3(+0.5f, +0.5f, +0.5f),
                new Vector3(+0.5f, +0.5f, -0.5f),
                new Vector3(+0.5f, -0.5f, -0.5f),
                new Vector3(+0.5f, -0.5f, +0.5f),
                // Back                                                               
                new Vector3(+0.5f, +0.5f, -0.5f),
                new Vector3(-0.5f, +0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3(+0.5f, -0.5f, -0.5f),
                // Front                                                              
                new Vector3(-0.5f, +0.5f, +0.5f),
                new Vector3(+0.5f, +0.5f, +0.5f),
                new Vector3(+0.5f, -0.5f, +0.5f),
                new Vector3(-0.5f, -0.5f, +0.5f),
            };

    }
}
