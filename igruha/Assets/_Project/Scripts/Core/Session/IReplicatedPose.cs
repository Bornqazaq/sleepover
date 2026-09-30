using UnityEngine;

namespace Igruha.Core.Session
{
    /// <summary>The last received simulation pose, independent of render interpolation.</summary>
    public interface IReplicatedPose
    {
        bool HasRemoteSample { get; }
        Vector3 ReceivedPosition { get; }
        Quaternion ReceivedRotation { get; }
        double ReceivedTime { get; }
    }
}
