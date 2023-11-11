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
        Quaternion camRot = new Quaternion(-0.023609221f, 0.46456802f, 0.012391418f, 0.8851359f);
        Quaternion camRotMouseDown;
        static float scale = 8000000.0f;
        Vector3 camPos = new Vector3(6293211, 159773.1f, 1549424.5f);
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

        float speed = 0.01f * scale;
        float Speed => LookAtDist * 0.01f;
        public void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.W)
            {
                Vector3 dir = Vector3.Transform(Vector3.UnitZ, camRot);
                wsMotion = -dir * Speed;
            }
            else if (e.Key == Key.S)
            {
                Vector3 dir = Vector3.Transform(Vector3.UnitZ, camRot);
                wsMotion = dir * Speed;
            }
            else if (e.Key == Key.A)
            {
                Vector3 dir = Vector3.Transform(Vector3.UnitX, camRot);
                adMotion = -dir * Speed;
            }
            else if (e.Key == Key.D)
            {
                Vector3 dir = Vector3.Transform(Vector3.UnitX, camRot);
                adMotion = dir * Speed;
            }
            else if (e.Key == Key.E)
            {
                Vector3 dir = Vector3.Transform(Vector3.UnitY, camRot);
                eqMotion = dir * Speed;
            }
            else if (e.Key == Key.Q)
            {
                Vector3 dir = Vector3.Transform(Vector3.UnitY, camRot);
                eqMotion = -dir * Speed;
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
            camPos += wsMotion;
            camPos += adMotion;
            camPos += eqMotion;
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
        public Matrix4x4 ViewMat { get
            {
                Matrix4x4 viewMat =
                        Matrix4x4.CreateFromQuaternion(camRot) *
                        Matrix4x4.CreateTranslation(camPos);
                Matrix4x4.Invert(viewMat, out viewMat);
                return viewMat;
            }
        }

    }
}
