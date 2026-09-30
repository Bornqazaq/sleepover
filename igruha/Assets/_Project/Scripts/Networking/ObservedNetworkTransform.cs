using Igruha.Core.Session;
using Unity.Netcode.Components;
using UnityEngine;

namespace Igruha.Networking
{
    /// <summary>
    /// Retains NGO state before visual smoothing, without adding any traffic or changing authority.
    /// NGO 2.x GetSpaceRelativePosition(true) returns the interpolated m_CurrentPosition for
    /// full precision transforms; capture the public state callback instead.
    /// </summary>
    public class ObservedNetworkTransform : NetworkTransform, IReplicatedPose
    {
        private Vector3 receivedPosition;
        private Quaternion receivedRotation;
        public double ReceivedTime { get; private set; }
        public bool HasRemoteSample => IsSpawned && !CanCommitToTransform;
        public Vector3 ReceivedPosition => InLocalSpace && transform.parent != null
            ? transform.parent.TransformPoint(receivedPosition) : receivedPosition;
        public Quaternion ReceivedRotation => InLocalSpace && transform.parent != null
            ? transform.parent.rotation * receivedRotation : receivedRotation;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            receivedPosition = GetSpaceRelativePosition();
            receivedRotation = GetSpaceRelativeRotation();
            ReceivedTime = NetworkManager.ServerTime.Time;
        }

        protected override void OnNetworkTransformStateUpdated(ref NetworkTransformState oldState, ref NetworkTransformState newState)
        {
            base.OnNetworkTransformStateUpdated(ref oldState, ref newState);
            double time = new Unity.Netcode.NetworkTime(NetworkManager.NetworkConfig.TickRate, newState.GetNetworkTick()).Time;
            if (newState.HasPositionChange)
            {
                Vector3 position = newState.UseHalfFloatPrecision ? GetSpaceRelativePosition(true) : newState.GetPosition();
                if (newState.UseHalfFloatPrecision || newState.HasPositionX) receivedPosition.x = position.x;
                if (newState.UseHalfFloatPrecision || newState.HasPositionY) receivedPosition.y = position.y;
                if (newState.UseHalfFloatPrecision || newState.HasPositionZ) receivedPosition.z = position.z;
            }
            ReceivedTime = time;
            if (!newState.HasRotAngleChange) return;
            if (newState.QuaternionSync) receivedRotation = newState.GetRotation();
            else
            {
                Vector3 euler = receivedRotation.eulerAngles, value = newState.GetRotation().eulerAngles;
                if (newState.HasRotAngleX) euler.x = value.x;
                if (newState.HasRotAngleY) euler.y = value.y;
                if (newState.HasRotAngleZ) euler.z = value.z;
                receivedRotation = Quaternion.Euler(euler);
            }
        }
    }
}
