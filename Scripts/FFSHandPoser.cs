using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.Grabbers;
using HurricaneVR.Framework.Core.HandPoser;
using HurricaneVR.Framework.Shared;
using UnityEngine;

public class FFSHandPoser : MonoBehaviour
{
    public HVRHandGrabber HandGrabber;
    public HVRHandAnimator HandAnimator;

    private FFSNetworkHandPoser _networkHandPoser;

    public void Awake()
    {
        HandGrabber.Grabbed.AddListener(OnGrabbed);
        HandGrabber.Released.AddListener(OnReleased);
    }

    public void OnGrabbed(HVRGrabberBase grabber, HVRGrabbable grabbable)
    {
        _networkHandPoser.RPCSetHandPoser(grabbable.gameObject);
    }

    public void OnReleased(HVRGrabberBase grabber, HVRGrabbable grabbable)
    {
        _networkHandPoser.RPCSetHandPoser(null);
    }

    private void Update()
    {
        if (_networkHandPoser == null)
        {
            if (FFSNetworkHandPoser.NetworkLeftHandSingleton != null && FFSNetworkHandPoser.NetworkRightHandSingleton != null)
            {
                if (HandGrabber.IsLeftHand)
                {
                    _networkHandPoser = FFSNetworkHandPoser.NetworkLeftHandSingleton;
                }
                else
                {
                    _networkHandPoser = FFSNetworkHandPoser.NetworkRightHandSingleton;
                }
                return;
            }
            return;
        }

        _networkHandPoser.RPCUpdateCurls(HandGrabber.IsLeftHand ? HVRController.LeftFingerCurls : HVRController.RightFingerCurls);
    }
}
