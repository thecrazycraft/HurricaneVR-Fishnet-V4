using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.Grabbers;
using UnityEngine;

public class NetworkGrabbable : NetworkBehaviour
{
    [SerializeField]
    private bool initializeUnparented = true;
    [Tooltip("When enabled, this game object will take over ownership of objects (with a Network Interaction Component) on collision, allowing things like doors to function seamlessly without any player input being required")]
    [SerializeField]
    private bool networkCollisionInteraction = false;

    private HVRGrabbable hvrGrabbable;
    private Rigidbody rb;

    private bool isSocketed;

    //[SyncVar(WritePermissions = WritePermission.ServerOnly)]
    private readonly SyncVar<int> socketId = new SyncVar<int>();

    private object socketLock = new object();

    private void Awake()
    {
        socketId.Value = -1;

        if (initializeUnparented)
        {
            transform.SetParent(null);
        }
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
        lock (socketLock)
        {
            if (socketId.Value > 0)
            {
                RPCUnSocket();
                isSocketed = false;
            }
            else
            {
                RPCSendTakeover();
            }
            if (rb)
            {
                rb.isKinematic = false;
            }
        }
    }

    private void OnSocketed(HVRSocket socket, HVRGrabbable grabbable)
    {
        lock (socketLock)
        {
            if (Owner.IsLocalClient && !isSocketed)
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
                isSocketed = true;

                //Debug.Log("Client tells server to socket", gameObject);
            }
        }
    }

    private void OnUnSocketed(HVRSocket socket, HVRGrabbable grabbable)
    {
        lock (socketLock)
        {
            if (socketId.Value > 0)
            {
                RPCUnSocket();
                //Debug.Log("Client tells server to unsocket", gameObject);
                isSocketed = false;
            }
        }
    }

    //------------------------------------- Server Functions -----------------------------------------
    public override void OnStartServer()
    {
        ServerManager.Objects.OnPreDestroyClientObjects += OnPreDestroyClientObjects;

        if (hvrGrabbable.StartingSocket != null)
        {
            if (hvrGrabbable.StartingSocket.TryGetComponent<NetworkObject>(out var networkObject))
            {
                socketId.Value = networkObject.ObjectId;
                TrySocket(socketId.Value);

                //If the starting socket is not linked remove it or it can cause issues

                if (!hvrGrabbable.LinkStartingSocket)
                {
                    //This causes issues on client hosted

                    if (!Owner.IsHost)
                    {
                        hvrGrabbable.StartingSocket = null;
                    }
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
        NetworkObject.GiveOwnership(conn);
        //Debug.Log("Server Grants Ownership to " + conn.ClientId, gameObject);
    }

    [ServerRpc(RequireOwnership = true)]
    public void RPCSocket(int _socketId)
    {
        lock (socketLock)
        {
            if (!isSocketed)
            {
                socketId.Value = _socketId;
                ObserversSocketedGrabbable(socketId.Value);
                isSocketed = true;

                //Debug.Log("RPC Socketing called with socket ID: " + socketId);
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void RPCUnSocket(NetworkConnection conn = null)
    {
        lock (socketLock)
        {
            if (socketId.Value > 0)
            {
                socketId.Value = -1;
                if (Owner.ClientId != conn.ClientId) NetworkObject.GiveOwnership(conn);
                ObserversUnSocketedGrabbable();

                //Debug.Log("RPC Unsocketing");
                isSocketed = false;
            }
            if (rb == null)
            {
                rb = GetComponent<Rigidbody>();
            }
            if (!Owner.IsValid && rb != null)
            {
                rb.isKinematic = false;
                //Debug.Log("Server is owner");
            }
        }
    }

    public override void OnOwnershipServer(NetworkConnection prevOwner)
    {
        base.OnOwnershipServer(prevOwner);
        if (rb == null) return;
        if (!Owner.IsValid || Owner.IsLocalClient)
        {
            if (socketId.Value > 0)
            {
                rb.isKinematic = true;
            }
            else
            {
                // This line is commented to avoid conflicts when client hosted
                // rb.isKinematic = false;
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

        if (Owner.IsLocalClient)
        {
            if (hvrGrabbable.StartingSocket)
            {
                isSocketed = true;
                hvrGrabbable.StartingSocket.TryGrab(hvrGrabbable, true, true);
            }
            return;
        }
        if (socketId.Value > 0)
        {
            TrySocket(socketId.Value, true);
        }
        else if (hvrGrabbable.IsSocketed)
        {
            TryUnSocket();
            if (hvrGrabbable.StartingSocket != null && !hvrGrabbable.LinkStartingSocket)
            {
                //Remove starting sockets that are no longer valid
                hvrGrabbable.StartingSocket = null;
            }
        }
    }

    [ObserversRpc(ExcludeOwner = true, ExcludeServer = false)]
    public void ObserversSocketedGrabbable(int _socketId)
    {
        TrySocket(_socketId);

        hvrGrabbable.CanBeGrabbed = false;
    }

    private void TrySocket(int _socketId, bool ignoreGrabSound = false)
    {
        lock (socketLock)
        {
            if (_socketId <= 0)
            {
                Debug.Log("Tried to socket invalid id", gameObject);
                return;
            }
            if (isSocketed) return;

            //Find the network object socket if this isn't socketed

            var netObjects = FindObjectsOfType<NetworkObject>(true);
            foreach (var netObj in netObjects)
            {
                if (netObj.ObjectId == _socketId)
                {
                    if (netObj.TryGetComponent<HVRSocket>(out var socket))
                    {
                        if (rb != null)
                        {
                            if ((NetworkManager.IsHostStarted && !Owner.IsValid) || Owner.IsLocalClient)
                            {
                                rb.isKinematic = false;
                            }
                            else
                            {
                                rb.isKinematic = true;
                            }
                        }
                        socket.TryGrab(hvrGrabbable, true, ignoreGrabSound);
                        if (rb != null) rb.isKinematic = true;
                        //Debug.Log("Socketed on client", gameObject);
                        break;
                    }
                }
            }
            //Parent this to the socket
            isSocketed = true;
        }
    }

    [ObserversRpc(ExcludeOwner = true, ExcludeServer = false)]
    public void ObserversUnSocketedGrabbable()
    {
        TryUnSocket();

        hvrGrabbable.CanBeGrabbed = true;
    }

    private void TryUnSocket()
    {
        lock (socketLock)
        {
            //Socketing can remove rigidbodies, we need to try and get it again
            if (rb == null)
            {
                rb = GetComponent<Rigidbody>();
            }

            if (!hvrGrabbable.IsHandGrabbed)
            {
                hvrGrabbable.ForceRelease();
                //Debug.Log("Unsocketed on client", gameObject);
            }
            hvrGrabbable.transform.SetParent(null);
            //Debug.Log("Unsocketing...");
            isSocketed = false;
        }
    }

    public override void OnOwnershipClient(NetworkConnection prevOwner)
    {
        base.OnOwnershipClient(prevOwner);
        if (prevOwner == Owner) return;

        if (rb)
        {
            if (((NetworkManager.IsHostStarted && !Owner.IsValid) || Owner.IsLocalClient) && !isSocketed)
            {
                rb.isKinematic = false;
            }
            else
            {
                rb.isKinematic = true;
            }
        }
    }

    // Give Ownership of objects this collided with to Owner
    private void OnCollisionEnter(Collision collision) // TODO: Fix this.
    {
        if (networkCollisionInteraction)
        {
            if (IsOwner)
            {
                if (collision.gameObject.CompareTag("DoorTag"))
                {
                    GameObject collisionObject = collision.gameObject;
                    //Debug.Log("Collision Object: " + collisionObject);
                    //if (collisionObject != null)
                    //{
                    //    FFSDoorBreachingHandler doorBreachingHandler = collisionObject.GetComponent<FFSDoorBreachingHandler>();

                    //    if (doorBreachingHandler != null)
                    //    {
                    //        //Debug.Log("Takeover called");
                    //        doorBreachingHandler.networkGrabbable.RPCSendTakeover();
                    //        doorBreachingHandler.networkGrabbable.rb.isKinematic = false;
                    //        doorBreachingHandler.RPCDoorTakeover();
                    //        //doorBreachingHandler.doorManager.RPCSendTakeover();
                    //    }
                    //}
                }
            }
        }
    }
}