using System.Collections.Generic;
using UnityEngine;

public static class AttackSlashCalibrationUtility
{
    public struct ShapeAnalysis
    {
        public bool isValid;
        public Vector3 center;
        public Vector3 forward;
        public Vector3 up;
        public float length;
        public int sampleCount;
    }

    public struct CalibrationResult
    {
        public bool isValid;
        public Vector3 localPositionOffset;
        public Vector3 rotationOffsetEuler;
        public float scaleMultiplier;
        public float quality;
        public string reason;
    }

    public static ShapeAnalysis AnalyzePointCloud(IReadOnlyList<Vector3> points, Vector3 fallbackForward, Vector3 fallbackUp)
    {
        if (points == null || points.Count == 0)
        {
            return new ShapeAnalysis
            {
                isValid = false,
                forward = SafeDirection(fallbackForward, Vector3.forward),
                up = SafeDirection(fallbackUp, Vector3.up),
                length = 0f,
                sampleCount = 0
            };
        }

        Vector3 center = Vector3.zero;
        for (int i = 0; i < points.Count; i++)
        {
            center += points[i];
        }
        center /= points.Count;

        Vector3 forward = FindPrincipalAxis(points, center, fallbackForward);
        Vector3 up = ProjectOnPlaneSafe(fallbackUp, forward, Vector3.up);
        float min = float.MaxValue;
        float max = float.MinValue;
        for (int i = 0; i < points.Count; i++)
        {
            float projection = Vector3.Dot(points[i] - center, forward);
            min = Mathf.Min(min, projection);
            max = Mathf.Max(max, projection);
        }

        return new ShapeAnalysis
        {
            isValid = true,
            center = center,
            forward = forward,
            up = up,
            length = Mathf.Max(0.001f, max - min),
            sampleCount = points.Count
        };
    }

    public static ShapeAnalysis AnalyzeTrajectory(IReadOnlyList<Vector3> samples, Vector3 fallbackForward, Vector3 fallbackUp)
    {
        ShapeAnalysis analysis = AnalyzePointCloud(samples, fallbackForward, fallbackUp);
        if (!analysis.isValid)
        {
            return analysis;
        }

        Vector3 chord = samples[samples.Count - 1] - samples[0];
        if (chord.sqrMagnitude > 0.0001f)
        {
            analysis.forward = chord.normalized;
        }

        Vector3 planeHint = FindStrongestDeviation(samples, analysis.center, analysis.forward);
        if (planeHint.sqrMagnitude > 0.0001f)
        {
            Vector3 normal = Vector3.Cross(analysis.forward, planeHint).normalized;
            Vector3 up = Vector3.Cross(normal, analysis.forward);
            if (Vector3.Dot(up, fallbackUp) < 0f)
            {
                up = -up;
            }
            analysis.up = SafeDirection(up, fallbackUp);
        }
        else
        {
            analysis.up = ProjectOnPlaneSafe(fallbackUp, analysis.forward, fallbackUp);
        }

        return analysis;
    }

    public static CalibrationResult CalculateFit(
        ShapeAnalysis weaponTrajectory,
        ShapeAnalysis slashShape,
        Vector3 spawnOriginPosition,
        Vector3 attackDirection,
        float strikeSpanLength = 0f,
        float outputScaleMultiplier = 1f)
    {
        if (!weaponTrajectory.isValid)
        {
            return Invalid("Weapon trajectory was not valid.");
        }

        if (!slashShape.isValid)
        {
            return Invalid("Slash shape was not valid.");
        }

        Vector3 safeAttackDirection = SafeHorizontalDirection(attackDirection, Vector3.forward);
        Vector3 targetForward = SafeDirection(weaponTrajectory.forward, safeAttackDirection);
        Vector3 targetUp = ProjectOnPlaneSafe(weaponTrajectory.up, targetForward, Vector3.up);
        Vector3 shapeForward = SafeDirection(slashShape.forward, Vector3.forward);
        Vector3 shapeUp = ProjectOnPlaneSafe(slashShape.up, shapeForward, Vector3.up);

        Quaternion attackRotation = Quaternion.LookRotation(safeAttackDirection, Vector3.up);
        Quaternion targetRotation = Quaternion.LookRotation(targetForward, targetUp);
        Quaternion shapeRotation = Quaternion.LookRotation(shapeForward, shapeUp);
        Quaternion worldRootRotation = targetRotation * Quaternion.Inverse(shapeRotation);
        Quaternion rotationOffset = Quaternion.Inverse(attackRotation) * worldRootRotation;

        float targetLength = ResolveTargetVisualLength(weaponTrajectory.length, strikeSpanLength);
        float fitScale = Mathf.Clamp(targetLength / Mathf.Max(0.001f, slashShape.length), 0.05f, 10f);
        float scale = Mathf.Clamp(fitScale * Mathf.Max(0.001f, outputScaleMultiplier), 0.05f, 20f);
        Vector3 rootPosition = weaponTrajectory.center - worldRootRotation * (slashShape.center * scale);
        Vector3 localOffset = Quaternion.Inverse(attackRotation) * (rootPosition - spawnOriginPosition);

        Vector3 fittedForward = (attackRotation * rotationOffset) * shapeForward;
        Vector3 fittedUp = (attackRotation * rotationOffset) * shapeUp;
        float directionScore = Mathf.Clamp01((Vector3.Dot(fittedForward.normalized, targetForward.normalized) + 1f) * 0.5f);
        float upScore = Mathf.Clamp01((Vector3.Dot(fittedUp.normalized, targetUp.normalized) + 1f) * 0.5f);
        float lengthRatio = Mathf.Min(targetLength, slashShape.length * fitScale) / Mathf.Max(targetLength, slashShape.length * fitScale, 0.001f);
        float quality = Mathf.Clamp01(directionScore * 0.45f + upScore * 0.25f + lengthRatio * 0.3f);

        return new CalibrationResult
        {
            isValid = true,
            localPositionOffset = localOffset,
            rotationOffsetEuler = NormalizeEuler(rotationOffset.eulerAngles),
            scaleMultiplier = scale,
            quality = quality,
            reason = "OK"
        };
    }

    public static float ResolveTargetVisualLength(float trajectoryLength, float strikeSpanLength)
    {
        float safeTrajectoryLength = Mathf.Max(0f, trajectoryLength);
        float safeStrikeSpanLength = Mathf.Max(0f, strikeSpanLength);
        return Mathf.Max(Mathf.Max(safeTrajectoryLength, safeStrikeSpanLength), 0.001f);
    }

    private static CalibrationResult Invalid(string reason)
    {
        return new CalibrationResult
        {
            isValid = false,
            scaleMultiplier = 1f,
            quality = 0f,
            reason = reason
        };
    }

    private static Vector3 FindPrincipalAxis(IReadOnlyList<Vector3> points, Vector3 center, Vector3 fallback)
    {
        Vector3 axis = SafeDirection(fallback, Vector3.forward);
        for (int iteration = 0; iteration < 8; iteration++)
        {
            Vector3 next = Vector3.zero;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 delta = points[i] - center;
                next += delta * Vector3.Dot(delta, axis);
            }

            if (next.sqrMagnitude <= 0.000001f)
            {
                break;
            }

            axis = next.normalized;
        }

        if (Vector3.Dot(axis, fallback) < 0f)
        {
            axis = -axis;
        }

        return axis;
    }

    private static Vector3 FindStrongestDeviation(IReadOnlyList<Vector3> points, Vector3 center, Vector3 forward)
    {
        Vector3 best = Vector3.zero;
        float bestMagnitude = 0f;
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 delta = points[i] - center;
            Vector3 deviation = delta - forward * Vector3.Dot(delta, forward);
            float magnitude = deviation.sqrMagnitude;
            if (magnitude > bestMagnitude)
            {
                best = deviation;
                bestMagnitude = magnitude;
            }
        }

        return best;
    }

    private static Vector3 SafeHorizontalDirection(Vector3 direction, Vector3 fallback)
    {
        direction.y = 0f;
        fallback.y = 0f;
        return SafeDirection(direction, SafeDirection(fallback, Vector3.forward));
    }

    private static Vector3 ProjectOnPlaneSafe(Vector3 vector, Vector3 normal, Vector3 fallback)
    {
        Vector3 projected = Vector3.ProjectOnPlane(vector, normal);
        return SafeDirection(projected, fallback);
    }

    private static Vector3 SafeDirection(Vector3 direction, Vector3 fallback)
    {
        if (direction.sqrMagnitude > 0.000001f)
        {
            return direction.normalized;
        }

        return fallback.sqrMagnitude > 0.000001f ? fallback.normalized : Vector3.forward;
    }

    private static Vector3 NormalizeEuler(Vector3 euler)
    {
        return new Vector3(NormalizeAngle(euler.x), NormalizeAngle(euler.y), NormalizeAngle(euler.z));
    }

    private static float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle > 180f)
        {
            angle -= 360f;
        }
        else if (angle < -180f)
        {
            angle += 360f;
        }

        return angle;
    }
}
