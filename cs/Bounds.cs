using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace googletiles
{

    public class Quad
    {
        Vector3[] pts;
        Vector3 u;
        Vector3 v;
        float du;
        float dv;
        Vector3 nrm;
        Vector3 center;
        public Quad(Vector3[] _pts)
        {
            pts = _pts;
            center = (pts[0] + pts[1] + pts[2] + pts[3]) * 0.25f;
            u = Vector3.Normalize(pts[2] - pts[1]);
            du = MathF.Abs(Vector3.Dot(pts[2] - center, u));
            v = Vector3.Normalize(pts[1] - pts[0]);
            dv = MathF.Abs(Vector3.Dot(pts[1] - center, v));
            nrm = Vector3.Cross(u, v);
            nrm = Vector3.Normalize(nrm);
        }

        public bool Intersect(Vector3 l0, Vector3 l, out float t)
        {
            // assuming vectors are all normalized
            float denom = Vector3.Dot(nrm, l);
            if (denom < 1e-6)
            {
                t = float.MaxValue;
                return false;
            }

            Vector3 p0l0 = center - l0;
            t = Vector3.Dot(p0l0, nrm) / denom;
            if (t < 0)
                return false;

            Vector3 ipt = l0 + l * t;
            float ddu = Vector3.Dot(ipt - center, u);
            float ddv = Vector3.Dot(ipt - center, v);
            if (MathF.Abs(ddu) < du && MathF.Abs(ddv) < dv)
                return true;
            return false;
        }

        public bool Side(Vector3 pt)
        {
            return Vector3.Dot((pt - center), nrm) < 0;
        }
    }

    public class Bounds : IEquatable<Bounds>
    {
        static Vector3 GlobalScale = new Vector3(7972671, 7972671, 7945940.5f);
        public Vector3 center;
        public Vector3 scale;
        public Vector3[] rot;
        public Matrix4x4 rotMat;
        public Matrix4x4 worldMat;
        public Matrix4x4 axisWorldMat;
        public Vector3[] pts;
        public Quad[] quads;
        static Vector3[] cubePts;

        bool IsGlobal => scale == GlobalScale;

        static int[][] PlaneIndices ={
            new int[]{ 0,1,3,2},
            new int[]{ 5,4,6,7},
            new int[]{ 0,2,6,4},
            new int[]{ 1,5,7,3},
            new int[]{ 0,4,5,1},
            new int[]{ 2,3,7,6}
        };
        static Bounds()
        {
            cubePts = new Vector3[8];
            for (int i = 0; i < 8; ++i)
            {
                cubePts[i] = new Vector3((i & 1) != 0 ? -0.5f : 0.5f,
                                ((i >> 1) & 1) != 0 ? -0.5f : 0.5f,
                                ((i >> 2) & 1) != 0 ? -0.5f : 0.5f);
            }
        }
        public Bounds(GoogleTile.BoundingVolume bv)
        {
            center = new Vector3(bv.box[0], bv.box[1], bv.box[2]);
            scale = new Vector3();
            rot = new Vector3[3];
            rotMat = Matrix4x4.Identity;
            Vector3[] scaledvecs = new Vector3[3];
            for (int i = 0; i < 3; ++i)
            {
                Vector3 vx = new Vector3(bv.box[3 + i * 3], bv.box[3 + i * 3 + 1], bv.box[3 + i * 3 + 2]);
                scale[i] = vx.Length();
                rot[i] = Vector3.Normalize(vx);
                rotMat[i, 0] = rot[i].X;
                rotMat[i, 1] = rot[i].Y;
                rotMat[i, 2] = rot[i].Z;
            }
            pts = new Vector3[8];

            worldMat =
                    Matrix4x4.CreateScale(scale * 2) *
                    rotMat *
                    Matrix4x4.CreateTranslation(center);

            float maxScale = MathF.Max(scale[0], MathF.Max(scale[1], scale[2]));
            axisWorldMat =
                    Matrix4x4.CreateTranslation(new Vector3(0.5f, 0, 0)) *
                    Matrix4x4.CreateScale(new Vector3(maxScale * 0.5f, maxScale * 0.05f, maxScale * 0.05f)) *
                    rotMat *
                    Matrix4x4.CreateTranslation(center);

            for (int i = 0; i < 8; ++i)
            {
                pts[i] = Vector3.Transform(cubePts[i], worldMat);
            }

            quads = new Quad[6];
            for (int idx = 0; idx < 6; ++idx)
            {
                Vector3[] vpts = new Vector3[4];
                for (int vidx = 0; vidx < 4; ++vidx)
                {
                    vpts[vidx] = pts[PlaneIndices[idx][vidx]];
                }
                quads[idx] = new Quad(vpts);
            }
        }

        public bool Intersect(Vector3 l0, Vector3 l, out float t)
        {
            float mint = float.MaxValue;
            bool foundinteresection = false;

            for (int i = 0; i < quads.Length; ++i)
            {
                float tt;
                if (quads[i].Intersect(l0, l, out tt) && tt > 0)
                {
                    Vector3 intersectPt = l0 + l * tt;
                    mint = Math.Min(tt, mint);
                    foundinteresection = true;
                }
            }
            t = mint;
            return foundinteresection;
        }
        public float GetScreenSpan(Matrix4x4 viewProj)
        {
            Vector4 spt0 = Vector4.Transform(new Vector4(pts[0], 1), viewProj);
            spt0 /= spt0.W;
            Vector4 spt7 = Vector4.Transform(new Vector4(pts[7], 1), viewProj);
            spt7 /= spt7.W;
            if (spt0.Z < 0 || spt7.Z < 0)
                return 0;
            return (new Vector2(spt7.X, spt7.Y) - new Vector2(spt0.X, spt0.Y)).LengthSquared();
        }

        bool PointInBounds(Vector3 pt)
        {
            float vx = Vector3.Dot(pt - center, rot[0]);
            if (MathF.Abs(vx) > scale[0])
                return false;
            float vy = Vector3.Dot(pt - center, rot[1]);
            if (MathF.Abs(vy) > scale[1])
                return false;
            float vz = Vector3.Dot(pt - center, rot[2]);
            if (MathF.Abs(vz) > scale[2])
                return false;
            return true;
        }

        public bool IsInside(CameraView cv)
        {
            return PointInBounds(cv.Pos);
        }
        public bool IsInView(CameraView cv)
        {
            if (IsGlobal)
                return true;
            if (IsInside(cv)) return true;
            if (Vector3.Dot(cv.Pos, center) < 0)
                return false;
            int[] sides = new int[6];
            int[] fsides = new int[6];
            for (int idx = 0; idx < pts.Length; ++idx)
            {
                Vector4 spt = Vector4.Transform(new Vector4(pts[idx], 1), cv.ViewProj);
                spt /= spt.W;
                if (spt.X < -1)
                    sides[0]++;
                else if (spt.X > 1)
                    sides[1]++;
                if (spt.Y < -1)
                    sides[2]++;
                else if (spt.Y > 1)
                    sides[3]++;
                if (spt.Z < 0)
                    sides[4]++;
                else if (spt.Z > 1)
                    sides[5]++;
                for (int i = 0; i < 6; ++i)
                {
                    if (cv.FrustumQuads[i].Side(pts[idx]))
                        fsides[i]++;
                }
            }
            for (int i = 0; i < sides.Length; ++i)
            {
                if (fsides[i] == 8)
                    return false;
            }

            return true;
        }
        public bool Equals(Bounds? other)
        {
            return center == other?.center &&
                scale == other?.scale;
        }
    }
}
