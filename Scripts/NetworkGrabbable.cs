using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.Grabbers;
using UnityEngine;

public class NetworkGrabbable : NetworkBehaviour
{
    private HVRGrabbable hvrGrabbable;
    private Rigidbody rb;

    private bool isSocketed;

    [SyncVar(WritePermissions = WritePermission.ServerOnly)]
    private int socketId = -1;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        hvrGrabbable = GetComponent<HVRGrabbable>();

        hvrGrabbable.Grabbed.AddListener(OnGrabbed);
        hvrGrabbable.Socketed.AddListener(OnSocketed);
        hvrGrabbable.UnSocketed.AddListener(OnUnSocketed);
    }

    private void OnDestroy()
    {
        hvrGrabbable.Grabbed.RemoveListener(OnGrabbed);
        hvrGrabbable.Socketed.RemoveListener(OnSocketed);
        hvrGrabbable.UnSocketed.RemoveListener(OnUnSocketed);
    }

    //------------------------------------- HVR Event Listeners -----------------------------------------
    private void OnGrabbed(HVRGrabberBase grabber, HVRGrabbable grabbable)
    {
        if (grabber.IsSocket) return;
        RPCSendTakeover();
        if (rb)
        {
            rb.isKinematic = false;
        }
        //Debug.Log("Grabbed", gameObject);
    }

    private void OnSocketed(HVRSocket socket, HVRGrabbable grabbable)
    {
        if (Owner.IsLocalClient)
        {
            var id = 0;
            if (socket.TryGetComponent<NetworkObject>(out var networkObject))
            {
                id = networkObject.ObjectId;
            }
            else
            {
                Debug.LogWarning("Socket does not have a network object");
            }

            RPCSocket(id);
            //Debug.Log("Socketed", gameObject);
        }
    }
    private void OnUnSocketed(HVRSocket socket, HVRGrabbable grabbable)
    {
        RPCUnSocket();
        //Debug.Log("UnSocketed", gameObject);      
    }

    //------------------------------------- Server Functions -----------------------------------------
    public override void OnStartServer()
    {
        ServerManager.Objects.OnPreDestroyClientObjects += OnPreDestroyClientObjects;
        if (hvrGrabbable.StartingSocket != null) {
            var id = 0;
            if (hvrGrabbable.StartingSocket.TryGetComponent<NetworkObject>(out var networkObject))
            {
                socketId = networkObject.ObjectId;
                TrySocket(socketId);
                //If the starting socket is not linked remove it or it can cause issues
                if (!hvrGrabbable.LinkStartingSocket)
                {
                    hvrGrabbable.StartingSocket = null;
                }
            }
            else
            {
                Debug.LogWarning("Socket does not have a network object");
            }
        }
    }
    public override void OnStopServer()
    {
        ServerManager.Objects.OnPreDestroyClientObjects -= OnPreDestroyClientObjects;
    }

    //Preserve grabbable network objects when the owner client disconnects
    private void OnPreDestroyClientObjects(NetworkConnection conn)
    {
        if (conn == Owner)
            RemoveOwnership();
    }

    [ServerRpc(RequireOwnership = false)]
    public void RPCSendTakeover(NetworkConnection conn = null)
    {
        //They are already the owner do nothing
        if (Owner.ClientId == conn.ClientId) return;
        if(socketId > -1)
        {
            socketId = -1;
            TryUnSocket();
            ObserversUnSocketedGrabbable();
        }
        NetworkObject.RemoveOwnership();
        NetworkObject.GiveOwnership(conn);
        //Debug.Log("Server Grants Ownership to " + conn.ClientId, gameObject);  
    }

    [ServerRpc(RequireOwnership = true)]
    public void RPCSocket(int _socketId)
    {
        socketId = _socketId;
        TrySocket(socketId);
        ObserversSocketedGrabbable(socketId);
    }
    [ServerRpc(RequireOwnership = true)]
    public void RPCUnSocket()
    {
        socketId = -1;
        TryUnSocket();
        ObserversUnSocketedGrabbable();
        //Socketing can remove rigidbodies, we need to try and get it again
        if (rb == null)
        {
            rb = GetComponent<Rigidbody>();
        }
        //The server is the owner
        if (!Owner.IsValid && rb != null)
        {
            rb.isKinematic = false;
        }
    }
    public override void OnOwnershipServer(NetworkConnection prevOwner)
    {
        base.OnOwnershipServer(prevOwner);
        if (rb == null) return;
        //The server has become the owner
        if (!Owner.IsValid)
        {
            if (socketId > - 1)
            {
                rb.isKinematic = true;
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
            }
            else
            {
                rb.isKinematic = false;
            }
        }
        else
        {
            rb.isKinematic = true;
        }
    }

    //------------------------------------- Client Functions -----------------------------------------
    public override void OnStartClient()
    {
        base.OnStartClient();
        if (socketId > -1)
        {
            TrySocket(socketId, true);
        }
        else
        {
            TryUnSocket();
            if (hvrGrabbable.StartingSocket != null && !hvrGrabbable.LinkStartingSocket)
            {
                //Remove starting sockets that are no longer valid
                hvrGrabbable.StartingSocket = null;
            }
        }
    }
    [ObserversRpc(ExcludeOwner = true)]
    public void ObserversSocketedGrabbable(int _socketId)
    {
        TrySocket(_socketId);
    }

    private void TrySocket(int _socketId, bool ignoreGrabSound = false)
    {
        //Find the network object socket if this isn't socketed
        if (!hvrGrabbable.IsSocketed)
        {
            var netObjects = FindObjectsOfType<NetworkObject>(true);
            foreach (var netObj in netObjects)
            {
                if (netObj.ObjectId == _socketId)
                {
                    if (netObj.TryGetComponent<HVRSocket>(out var socket))
                    {
                        if (rb != null)
                        {
                            if (Owner.IsLocalClient)
                            {
                                rb.isKinematic = false;
                            }
                            else
                            {
                                rb.isKinematic = true;
                            }
                        }
                        socket.TryGrab(hvrGrabbable, true, ignoreGrabSound);
                        if(rb != null) rb.isKinematic = true;
                        //Debug.Log("Socketed on client", gameObject);
                        break;
                    }
                }
            }
        }
        //Parent this to the socket
        isSocketed = true;
    }

    [ObserversRpc(ExcludeOwner = true)]
    public void ObserversUnSocketedGrabbable()
    {
        TryUnSocket();
        isSocketed = false;
    }

    private void TryUnSocket()
    {
        //Socketing can remove rigidbodies, we need to try and get it again
        if(rb == null)
        {
            rb = GetComponent<Rigidbody>();
        }
        if (hvrGrabbable.IsSocketed)
        {
            //Debug.Log("Unsocketed on client");
            hvrGrabbable.Socket.ForceRelease();
        }
    }

    public override void OnOwnershipClient(NetworkConnection prevOwner)
    {
        base.OnOwnershipClient(prevOwner);
        if (rb)
        {
            if (Owner.IsLocalClient && !isSocketed)
            {
                rb.isKinematic = false;
            }
            else
            {
                rb.isKinematic = true;
            }
        }
        //Debug.Log("Client Ownership sets kinematic " + rb.isKinematic, gameObject);
    }
}
