using FishNet.Object;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.HandPoser;
using UnityEngine;

public class FFSNetworkHandPoser : NetworkBehaviour
{
    public bool isLeft;

    public static FFSNetworkHandPoser NetworkLeftHandSingleton;
    public static FFSNetworkHandPoser NetworkRightHandSingleton;

    public HVRHandAnimator HandAnimator;

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (IsOwner)
        {
            if (isLeft)
            {
                NetworkLeftHandSingleton = this;
            }
            else
            {
                NetworkRightHandSingleton = this;
            }
        }
    }

    [ServerRpc(RequireOwnership = true)]
    public void RPCSetHandPoser(GameObject grabbable)
    {
        ObserversSetHandPoser(grabbable);
    }

    /// <summary>
    /// If grabbable is null, reset hand poser
    /// </summary>
    [ObserversRpc(ExcludeOwner = true)]
    public void ObserversSetHandPoser(GameObject grabbable)
    {
        if (grabbable == null) // if the pose doesn't sync, it might not have a network object, make sure it does! (It does not require a network grabbable setup, just the network object so that it can be serialized and passed over the network.)
        {
            HandAnimator.ResetToDefault();
            return;
        }

        if (grabbable.TryGetComponent(out HVRGrabbable _hVRGrabbabble))
        {
            if (_hVRGrabbabble.GrabPointsMeta.Count > 0)
            {
                HandAnimator.SetCurrentPoser(_hVRGrabbabble.GrabPointsMeta[0].HandPoser);
            }
        }
    }

    [ServerRpc(RequireOwnership = true)]
    public void RPCUpdateCurls(float[] curls)
    {
        ObserversUpdateCurls(curls);
    }

    [ObserversRpc(ExcludeOwner = true, ExcludeServer = false)]
    public void ObserversUpdateCurls(float[] curls)
    {
        HandAnimator.FingerCurlSource = curls;
    }
}
