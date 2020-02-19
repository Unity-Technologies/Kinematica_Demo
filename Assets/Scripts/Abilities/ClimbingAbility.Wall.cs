using Unity.SnapshotDebugger;
using Unity.Mathematics;
using Unity.Kinematica;

using UnityEngine;

public partial class ClimbingAbility : SnapshotProvider, Ability
{
    public struct WallAnchor
    {
        public float u;
        public float v;

        public static WallAnchor Create()
        {
            return new WallAnchor();
        }

        public void WriteToStream(Buffer buffer)
        {
            buffer.Write(u);
            buffer.Write(v);
        }

        public void ReadFromStream(Buffer buffer)
        {
            u = buffer.ReadSingle();
            v = buffer.ReadSingle();
        }
    }

    public struct WallGeometry
    {
        public AffineTransform transform;
        public float3 scale;

        public float3 normal;
        public float3 center;
        public float3 size;

        public float3 transformPoint(float3 p)
        {
            return transform.transform(p * scale);
        }

        public float3 inverseTransformPoint(float3 p)
        {
            return transform.inverseTransform(p) / scale;
        }

        public float3 transformDirection(float3 d)
        {
            return transform.transformDirection(d);
        }

        public static AffineTransform Convert(Transform transform)
        {
            return new AffineTransform(transform.position, transform.rotation);
        }

        public static WallGeometry Create()
        {
            return new WallGeometry();
        }

        public void Initialize(BoxCollider collider, AffineTransform contactTransform)
        {
            transform = Convert(collider.transform);
            scale = collider.transform.lossyScale;

            center = collider.center;
            size = collider.size;

            Initialize(contactTransform);
        }

        public void Initialize(AffineTransform contactTransform)
        {
            // World space contact position to local canonical cube position
            float3 localPosition =
                WorldToLocal(contactTransform.t);

            normal = GetNormal(0);
            float minimumDistance = math.abs(
                PlaneDistance(normal,
                    Missing.up, localPosition));
            for (int i = 1; i < 4; ++i)
            {
                float3 n = GetNormal(i);
                float distance = math.abs(
                    PlaneDistance(n,
                        Missing.up, localPosition));
                if (distance < minimumDistance)
                {
                    normal = n;
                    minimumDistance = distance;
                }
            }
        }

        public WallAnchor GetAnchor(float3 position)
        {
            float3 localPosition =
                WorldToLocal(position);

            float distance = math.abs(
                PlaneDistance(normal,
                    Missing.up, localPosition));

            localPosition -= normal * distance;

            WallAnchor result;

            result.u = 1.0f - math.saturate(((
                math.dot(GetOrthogonalLocalSpace(),
                    localPosition) + 1.0f) * 0.5f));

            result.v = 1.0f - math.saturate(
                (localPosition.y + 1.0f) * 0.5f);

            return result;
        }

        public WallAnchor UpdateAnchor(WallAnchor anchor, float2 uv)
        {
            WallAnchor result;

            float width = GetWidth();
            float height = GetHeight();

            float ud = uv.x / width;
            float vd = uv.y / height;

            result.u = math.saturate(anchor.u - ud);
            result.v = math.saturate(anchor.v - vd);

            return result;
        }

        public static float3 GetNormal(int index)
        {
            float3[] normals = new float3[]
            {
                Missing.right,
                -Missing.right,
                Missing.forward,
                -Missing.forward
            };

            return normals[index];
        }

        public static float PlaneDistance(float3 normal, float3 up, float3 position)
        {
            float3 orthogonal = math.cross(normal, up);

            // u*V1 + v*V2, where u and v = 1.0f
            float3 vertex = normal + up + orthogonal;

            float d = -math.dot(normal, vertex);

            return math.dot(normal, position) + d;
        }

        public float3 WorldToLocal(float3 p)
        {
            return Missing.mul(
                inverseTransformPoint(p) - center,
                    Missing.recip(size)) * 2.0f;
        }

        public float3 GetOrthogonalLocalSpace()
        {
            return math.cross(normal, Missing.up);
        }

        public float3 GetOrthogonalWorldSpace()
        {
            return
                transformDirection(
                    GetOrthogonalLocalSpace());
        }

        public float3 GetBaseVertex()
        {
            // u*V1 + v*V2, where u and v = 1.0f
            return normal + Missing.up + GetOrthogonalLocalSpace();
        }

        public float3 GetPosition(WallAnchor anchor)
        {
            float3 orthogonal = GetOrthogonalLocalSpace();
            float3 vertex = GetBaseVertex();

            float3 v0 =
                transformPoint(
                    center + Missing.mul(size, vertex) * 0.5f);
            float3 o = transformDirection(orthogonal);
            float3 up = Missing.up;

            float u = GetWidth() * anchor.u;
            float v = GetHeight() * anchor.v;

            return v0 - (o * u) - (up * v);
        }

        public float3 GetNormalLocalSpace()
        {
            return normal;
        }

        public float3 GetNormalWorldSpace()
        {
            return
                transformDirection(
                    GetNormalLocalSpace());
        }

        public void WriteToStream(Buffer buffer)
        {
            buffer.Write(transform);
            buffer.Write(scale);

            buffer.Write(normal);
            buffer.Write(center);
            buffer.Write(size);
        }

        public void ReadFromStream(Buffer buffer)
        {
            transform = buffer.ReadAffineTransform();
            scale = buffer.ReadVector3();

            normal = buffer.ReadVector3();
            center = buffer.ReadVector3();
            size = buffer.ReadVector3();
        }

        public static float3 GetNextVertexCCW(float3 normal, float3 vertex)
        {
            // https://math.stackexchange.com/questions/304700
            return normal + math.cross(normal, vertex);
        }

        public float GetWidth()
        {
            float3 orthogonal = GetOrthogonalLocalSpace();

            return math.dot(orthogonal, size) *
                math.dot(orthogonal, scale);
        }

        public float GetHeight()
        {
            return size.y * scale.y;
        }

        public float GetHeight(ref WallAnchor anchor)
        {
            return GetHeight() * (1.0f - anchor.v);
        }

        public void DebugDraw()
        {
            float3 vertex = GetBaseVertex();

            for (int i = 0; i < 4; ++i)
            {
                float3 nextVertex = GetNextVertexCCW(normal, vertex);

                float3 v0 = transformPoint(center + Missing.mul(size, vertex) * 0.5f);
                float3 v1 = transformPoint(center + Missing.mul(size, nextVertex) * 0.5f);

                Debug.DrawLine(v0, v1, Color.green);

                vertex = nextVertex;
            }
        }

        public void DebugDraw(ref WallAnchor state)
        {
            float3 position = GetPosition(state);
            float3 normal = GetNormalWorldSpace();

            Debug.DrawLine(position, position + normal * 0.3f, Color.red);
        }
    }
}
