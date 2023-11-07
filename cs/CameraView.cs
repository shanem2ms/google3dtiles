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
        Quaternion camRot = Quaternion.Identity;
        Quaternion camRotMouseDown;
        static float scale = 8000000.0f;
        Vector3 camPos = new Vector3(0, 0, 2 * scale);
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
                    0.05f * scale,
                    10f * scale);
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
