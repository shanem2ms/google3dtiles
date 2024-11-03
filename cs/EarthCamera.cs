using System;
using System.Numerics;

namespace googletiles
{
    class EarthCamera
    {
        public Vector3 focus;
        public float azimuth = 0;
        public float elevation = 0;
        public float roll;
        public float range;

        const float earthRadius = 6371000;
        const float PiOverTwo = MathF.PI * 0.5f;

        Quaternion RotateWorld(Vector3 gsPoint)
        {
            return new Quaternion(Vector3.UnitY, PiOverTwo) *
                   new Quaternion(Vector3.UnitZ, PiOverTwo) *
                   new Quaternion(Vector3.UnitY, -gsPoint.X) *
                   new Quaternion(Vector3.UnitX, gsPoint.Y);
        }

        Vector3 ToWorld(float longitude, float latitude, float radius)
        {
            float sinLat = MathF.Sin(latitude);
            float cosLatRadius = MathF.Cos(latitude) * radius;
            float sinLon = MathF.Sin(longitude);
            float cosLon = MathF.Cos(longitude);

            Vector3 wsPoint = new Vector3(
                    cosLatRadius* cosLon,
                    cosLatRadius* sinLon,
                    -sinLat * radius);

            return wsPoint;
        }

        public void ToModel(out Vector3 wsPosition, out Vector3 viewDir, out Vector3 upDir)
        {
            // The input azimuth is set up so that looking to the north is at 0 degrees
            // and looking to the east is at 90 degrees, that is, a compass heading.  
            // Turn it into a Cartesian angle.
            // NOTE: There is some underhanded stuff going on here because the Rotate3D
            // method really goes from left handed geo space to world space.  This occurs
            // because the switch to left handed did not catch this issue.  MRK.
            float cartesianAzimuth = -azimuth - PiOverTwo;
            float azimuthCos = MathF.Cos(cartesianAzimuth);
            float azimuthSin = MathF.Sin(cartesianAzimuth);

            Vector3 eyeLHGsRay = new Vector3(
                    MathF.Cos(elevation) * azimuthCos,
                    MathF.Sin(elevation) * azimuthSin,
                    MathF.Sin(-elevation));

            Matrix4x4 gsToWsMatrix = Matrix4x4.CreateFromQuaternion(RotateWorld(focus));
            Vector3 eyeWsRay = Vector3.TransformNormal(eyeLHGsRay, gsToWsMatrix);

            wsPosition = (eyeWsRay * range) + ToWorld(focus.X, focus.Y, focus.Z + earthRadius);
            viewDir = -eyeWsRay;

            // The up vector is 90 degrees more elevated (which works out to be a
            // de-elevation 180 degrees around) than the view direction.
            float upElevation = elevation + PiOverTwo;

            Vector3 upLHGsRay = new Vector3(
                    MathF.Cos(upElevation) * azimuthCos,
                    MathF.Cos(upElevation) * azimuthSin,
            MathF.Sin(-upElevation));

            // Roll the camera.
            upDir = Vector3.TransformNormal(upLHGsRay, 
                (Matrix4x4.CreateFromQuaternion(new Quaternion(-eyeLHGsRay, roll)) * gsToWsMatrix));

        }
    }
}

