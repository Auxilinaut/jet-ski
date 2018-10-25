// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;
using Crest;
using System.Net;
using System.Net.Sockets;
using System.IO;

namespace JetSki
{
    public class BoatAlignNormal : MonoBehaviour
    {
        [Serializable]
        public struct Inputs
        {
            public float forward;
            public float sideways;
            public bool waterBoosting;
            public bool rocketBoosting;
        }

        [Serializable]
        public struct InputMessage: ISerializable
        {
            public float delivery_time;
            public uint start_tick_number;
            public List<Inputs> inputs;

            // this method is called during serialization
            //[SecurityPermission(SecurityAction.Demand, SerializationFormatter = true)]
            public void GetObjectData(SerializationInfo info, StreamingContext context)
            {
                info.AddValue("delivery_time", delivery_time);
                info.AddValue("start_tick_number", start_tick_number);
                info.AddValue("inputs", inputs);
            }

            // this constructor is used for deserialization
            public InputMessage(SerializationInfo info, StreamingContext text) : this()
            {
                delivery_time = (float)info.GetValue("delivery_time", typeof(float));
                start_tick_number = (uint)info.GetValue("start_tick_number", typeof(uint));
                inputs = (List<Inputs>)info.GetValue("inputs", typeof(List<Inputs>));
            }
        }

        public struct ClientState
        {
            public Vector3 position;
            public Quaternion rotation;
        }

        [Serializable]
        public struct StateMessage: ISerializable
        {
            public float delivery_time;
            public uint tick_number;
            public SerializableVector3 position;
            public SerializableQuaternion rotation;
            public SerializableVector3 velocity;
            public SerializableVector3 angular_velocity;

            // this method is called during serialization
            //[SecurityPermission(SecurityAction.Demand, SerializationFormatter = true)]
            public void GetObjectData(SerializationInfo info, StreamingContext context)
            {
                info.AddValue("delivery_time", delivery_time);
                info.AddValue("tick_number", tick_number);
                info.AddValue("position", position);
                info.AddValue("rotation", rotation);
                info.AddValue("velocity", velocity);
                info.AddValue("angular_velocity", angular_velocity);
            }

            // this constructor is used for deserialization
            public StateMessage(SerializationInfo info, StreamingContext text) : this()
            {
                delivery_time = (float)info.GetValue("delivery_time", typeof(float));
                tick_number = (uint)info.GetValue("tick_number", typeof(uint));
                position = (SerializableVector3)info.GetValue("position", typeof(SerializableVector3));
                rotation = (SerializableQuaternion)info.GetValue("rotation", typeof(SerializableQuaternion));
                velocity = (SerializableVector3)info.GetValue("velocity", typeof(SerializableVector3));
                angular_velocity = (SerializableVector3)info.GetValue("angular_velocity", typeof(SerializableVector3));
            }
        }

        public float _bottomH = -1f;
        public bool _debugDraw = false;
        public float _overrideProbeRadius = -1f;
        public float _buoyancyCoeff = 40000f;
        public float _boyancyTorque = 2f;

        public float _forceHeightOffset = -1f;
        public float _enginePower = 10000f;
        public float _turnPower = 100f;

        public float _boatWidth = 2f;

        public float _dragInWaterUp = 20000f;
        public float _dragInWaterRight = 20000f;
        public float _dragInWaterForward = 20000f;
        public bool InWater { get; private set; }
        public Vector3 VelocityRelativeToWater { get; private set; }

        Vector3 _displacementToBoat, _displacementToBoatLastFrame;
        bool _displacementToBoatInitd = false;
        public Vector3 DisplacementToBoat { get { return _displacementToBoat; } }

        private bool RocketBoosting { get; set; }
        private bool WaterBoosting { get; set; }

        public bool _playerControlled = true;
        public float _throttleBias = 0f;
        public float _steerBias = 0f;
        public Vector3 RocketThrust = new Vector3(0, 0, 100000f);
        public float WaterThrust = 15f;
        private bool CameraLockedOnBall = false;

        // client specific
        public GameObject client_player;
        public GameObject client_proxy;
        private Rigidbody client_proxy_rigidbody;
        public bool client_enable_corrections = true;
        public bool client_correction_smoothing = false;
        public bool client_send_redundant_inputs = false;
        private float client_timer;
        private uint client_tick_number;
        private uint client_last_received_state_tick;
        private const int c_client_buffer_size = 1024; //1024;
        private ClientState[] client_state_buffer; // client stores predicted moves here
        private Inputs[] client_input_buffer; // client stores predicted inputs here
        private Queue<StateMessage> client_state_msgs;
        private Vector3 client_pos_error;
        private Quaternion client_rot_error;

        // server specific
        //public GameObject server_player;
        Rigidbody _rb;
        public uint server_snapshot_rate = 0; //64hz;
        private uint server_tick_number;
        private uint server_tick_accumulator;
        private Queue<InputMessage> server_input_msgs;

        // other networking bologna
        private string IPconnect = "127.0.0.1";
        private List<int> _pingTime = new List<int>();
        public float latency = 0.05f;

        //MemoryStream memoryStream = new MemoryStream();
        //BinaryFormatter bf = new BinaryFormatter();

        #region Data
        public static BoatAlignNormal instance;

        public bool isServer;

        /// <summary>
        /// IP for clients to connect to. Null if you are the server.
        /// </summary>
        public IPAddress serverIp;

        /// <summary>
        /// For Clients, there is only one and it's the connection to the server.
        /// For Servers, there are many - one per connected client.
        /// </summary>
        List<IPEndPoint> clientList = new List<IPEndPoint>();

        public static byte[] theData;

        UdpConnectedClient connection;
        #endregion

        public void Awake()
        {
            Application.targetFrameRate = 60;
            instance = this;

            if (!_playerControlled)
            {
                this.isServer = true;
                connection = new UdpConnectedClient();
            }
            else
            {
                serverIp = IPAddress.Parse(IPconnect);
                connection = new UdpConnectedClient(ip: serverIp);
                instance.clientList.Add(new IPEndPoint(serverIp, Globals.port));
                //AddClient(new IPEndPoint(serverIp, Globals.port));
            }
        }

        void Start()
        {
            //this.StartCoroutine(PingUpdate(IPconnect));

            if (this.isServer)
            {
                //IPconnect = "127.0.0.1";
                //this.StartCoroutine(PingUpdate(IPconnect));
                //_rb = this.server_player.GetComponent<Rigidbody>();
                _rb = GetComponent<Rigidbody>();

                this.server_tick_number = 0;
                this.server_tick_accumulator = 0;
                this.server_input_msgs = new Queue<InputMessage>();

                return;
            }

            //IPconnect = "127.0.0.1";
            //this.StartCoroutine(PingUpdate(IPconnect));
            this.client_proxy_rigidbody = this.client_proxy.GetComponent<Rigidbody>();
            this.client_timer = 0.0f;
            this.client_tick_number = 0;
            this.client_last_received_state_tick = 0;
            this.client_state_buffer = new ClientState[c_client_buffer_size];
            this.client_input_buffer = new Inputs[c_client_buffer_size];
            this.client_state_msgs = new Queue<StateMessage>();
            this.client_pos_error = Vector3.zero;
            this.client_rot_error = Quaternion.identity;
        }

        private void Update()
        {
            /*if (_pingTime.Count == 0)
            {
                return;
            }*/
            float dt = Time.fixedDeltaTime;

            if (_playerControlled) /* ************CLIENT UPDATE************ */
            {
                float client_timer = this.client_timer;
                uint client_tick_number = this.client_tick_number;
                client_timer += Time.deltaTime;

                CameraLockedOnBall = Input.GetKeyDown(KeyCode.Space) | Input.GetButtonDown("Jump");

                while (client_timer >= dt)
                {
                    client_timer -= dt;

                    uint buffer_slot = client_tick_number % c_client_buffer_size;

                    Inputs inputs = new Inputs();
                    inputs.forward = 0;
                    inputs.sideways = 0;
                    inputs.forward += Input.GetAxis("Vertical");
                    inputs.sideways += Input.GetAxis("Horizontal");
                    inputs.rocketBoosting = Input.GetMouseButton(0) | Input.GetButton("Fire2");
                    inputs.waterBoosting = Input.GetMouseButton(1) | Input.GetButton("Fire1");
                    this.client_input_buffer[buffer_slot] = inputs;

                    // store state for this tick, then use current state + input to step simulation
                    this.ClientStoreCurrentStateAndStep(
                        ref this.client_state_buffer[buffer_slot],
                        client_proxy_rigidbody,
                        inputs,
                        dt, client_timer);

                    // send input packet to server
                    InputMessage input_msg;
                    input_msg.delivery_time = Time.time + this.latency;// + 0.1f;//this._pingTime[this._pingTime.Count - 1];
                    input_msg.start_tick_number = this.client_send_redundant_inputs ? this.client_last_received_state_tick : client_tick_number;
                    input_msg.inputs = new List<Inputs>();

                    for (uint tick = input_msg.start_tick_number; tick <= client_tick_number; ++tick)
                    {
                        input_msg.inputs.Add(this.client_input_buffer[tick % c_client_buffer_size]);
                    }

                    //***SEND THE STUFF (UNTESTED)***
                    Debug.Log("Sending input_msg");
                    /*Debug.Log("im delivery_time " + input_msg.delivery_time);
                    Debug.Log("im inputs " + input_msg.inputs);
                    Debug.Log("im start_tick_number " + input_msg.start_tick_number);*/
                    using (var memoryStream = new MemoryStream())
                    {
                        var bf = new BinaryFormatter();
                        bf.Serialize(memoryStream, input_msg);
                        connection.Send(memoryStream.ToArray(), clientList[0]);
                    }
                    ++client_tick_number;
                }

                //***RECEIVE THE STUFF (UNTESTED)***
                if (theData != null)
                {
                    using (var memoryStream = new MemoryStream(theData))
                    {
                        var bf = new BinaryFormatter();
                        //memoryStream.Write(theData, 0, theData.Length);
                        memoryStream.Seek(0, 0);
                        Debug.Log("Received state message");
                        /*Debug.Log("angular_velocity " + sm.angular_velocity);
                        Debug.Log("delivery_time " + sm.delivery_time);
                        Debug.Log("position " + sm.position);
                        Debug.Log("rotation " + sm.rotation);
                        Debug.Log("tick_number " + sm.tick_number);
                        Debug.Log("velocity " + sm.velocity);*/
                        this.client_state_msgs.Enqueue((StateMessage)bf.Deserialize(memoryStream));
                        theData = null;
                    }
                }

                if (this.ClientHasStateMessage())
                {
                    StateMessage state_msg = this.client_state_msgs.Dequeue();
                    while (this.ClientHasStateMessage()) // make sure if there are any newer state messages available, we use those instead
                    {
                        state_msg = this.client_state_msgs.Dequeue();
                    }

                    this.client_last_received_state_tick = state_msg.tick_number;

                    //this.proxy_player.rigidbody.transform.position = state_msg.position;
                    //this.proxy_player.rigidbody.transform.rotation = state_msg.rotation;

                    if (this.client_enable_corrections)
                    {
                        uint buffer_slot = state_msg.tick_number % c_client_buffer_size;
                        Vector3 position_error = state_msg.position - this.client_state_buffer[buffer_slot].position;
                        float rotation_error = 1.0f - Quaternion.Dot(state_msg.rotation, this.client_state_buffer[buffer_slot].rotation);

                        if (position_error.sqrMagnitude > 0.0000001f || rotation_error > 0.00001f)
                        {
                            // capture the current predicted pos for smoothing
                            Vector3 prev_pos = client_proxy_rigidbody.transform.position + this.client_pos_error;
                            Quaternion prev_rot = client_proxy_rigidbody.transform.rotation * this.client_rot_error;

                            // rewind & replay
                            client_proxy_rigidbody.transform.position = state_msg.position;
                            client_proxy_rigidbody.transform.rotation = state_msg.rotation;
                            client_proxy_rigidbody.velocity = state_msg.velocity;
                            client_proxy_rigidbody.angularVelocity = state_msg.angular_velocity;

                            uint rewind_tick_number = state_msg.tick_number;
                            //StartCoroutine(DoTheShit(rewind_tick_number,dt));
                            while (rewind_tick_number < client_tick_number)
                            {
                                buffer_slot = rewind_tick_number % c_client_buffer_size;
                                this.ClientStoreCurrentStateAndStep(
                                    ref this.client_state_buffer[buffer_slot],
                                    client_proxy_rigidbody,
                                    this.client_input_buffer[buffer_slot],
                                    dt, client_timer);

                                ++rewind_tick_number;
                            }

                            // if more than 2ms apart, just snap
                            if ((prev_pos - client_proxy_rigidbody.transform.position).sqrMagnitude > 250.0f)//.sqrMagnitude >= 4.0f)
                            {
                                this.client_pos_error = Vector3.zero;
                                this.client_rot_error = Quaternion.identity;
                            }
                            else
                            {
                                this.client_pos_error = prev_pos - client_proxy_rigidbody.transform.position;
                                this.client_rot_error = Quaternion.Inverse(client_proxy_rigidbody.transform.rotation) * prev_rot;
                            }
                        }
                    }
                }

                this.client_timer = client_timer;
                this.client_tick_number = client_tick_number;

                if (this.client_correction_smoothing)
                {
                    this.client_pos_error *= 0.9f;
                    this.client_rot_error = Quaternion.Slerp(this.client_rot_error, Quaternion.identity, 0.1f);
                }
                else
                {
                    this.client_pos_error = Vector3.zero;
                    this.client_rot_error = Quaternion.identity;
                }

                client_player.transform.position = client_proxy_rigidbody.transform.position + this.client_pos_error;
                client_player.transform.rotation = client_proxy_rigidbody.transform.rotation * this.client_rot_error;
            }
            else /* ************SERVER UPDATE************ */
            {
                uint server_tick_number = this.server_tick_number;
                uint server_tick_accumulator = this.server_tick_accumulator;
                // Rigidbody server_rigidbody = this.server_player.GetComponent<Rigidbody>();

                //***RECEIVE THE STUFF (UNTESTED)***
                if (theData != null)
                {
                    using (var memoryStream = new MemoryStream(theData))
                    {
                        var bf = new BinaryFormatter();
                        //memoryStream.Write(theData, 0, theData.Length);
                        memoryStream.Seek(0, 0);
                        this.server_input_msgs.Enqueue((InputMessage)bf.Deserialize(memoryStream));
                        theData = null;
                    }
                }

                while (this.server_input_msgs.Count > 0 && Time.time >= this.server_input_msgs.Peek().delivery_time)
                {
                    InputMessage input_msg = this.server_input_msgs.Dequeue();

                    // message contains an array of inputs, calculate what tick the final one is
                    uint max_tick = input_msg.start_tick_number + (uint)input_msg.inputs.Count - 1;

                    // if that tick is greater than or equal to the current tick we're on, then it
                    // has inputs which are new
                    if (max_tick >= server_tick_number)
                    {
                        // there may be some inputs in the array that we've already had,
                        // so figure out where to start
                        uint start_i = server_tick_number > input_msg.start_tick_number ? (server_tick_number - input_msg.start_tick_number) : 0;

                        // run through all relevant inputs, and step player forward
                        for (int i = (int)start_i; i < input_msg.inputs.Count; ++i)
                        {
                            this.PrePhysicsStep(_rb, input_msg.inputs[i], dt, Time.deltaTime);
                            //Physics.SyncTransforms();
                            Physics.Simulate(dt);

                            ++server_tick_accumulator;
                            if (server_tick_accumulator >= this.server_snapshot_rate)
                            {
                                server_tick_accumulator = 0;

                                StateMessage state_msg;
                                state_msg.delivery_time = Time.time + this.latency;// + 0.1f;//+ this._pingTime[this._pingTime.Count-1];
                                state_msg.tick_number = server_tick_number;
                                state_msg.position = _rb.transform.position;
                                state_msg.rotation = _rb.transform.rotation;
                                state_msg.velocity = _rb.velocity;
                                state_msg.angular_velocity = _rb.angularVelocity;

                                //***SEND THE STUFF (UNTESTED)***
                                using (var memoryStream = new MemoryStream())
                                {
                                    var bf = new BinaryFormatter();
                                    bf.Serialize(memoryStream, state_msg);
                                    connection.Send(memoryStream.ToArray(), clientList[0]);
                                }
                            }
                        }

                        //this.server_display_player.rigidbody.transform.position = server_rigidbody.position;
                        //this.server_display_player.rigidbody.transform.rotation = server_rigidbody.rotation;

                        server_tick_number = max_tick + 1;
                    }
                }

                this.server_tick_number = server_tick_number;
                this.server_tick_accumulator = server_tick_accumulator;
            }
        }

        private void OnApplicationQuit()
        {
            connection.Close();
        }

        private void PrePhysicsStep(Rigidbody rigidbody, Inputs inputs, float fdt, float dt)
        {
            if (inputs.waterBoosting)
            {
                if (inputs.forward != 0)
                {
                    rigidbody.AddRelativeForce(0, WaterThrust * fdt, 0, ForceMode.VelocityChange);
                    rigidbody.AddRelativeForce(0, 0, WaterThrust * fdt * inputs.forward, ForceMode.VelocityChange);
                    rigidbody.AddRelativeTorque(Vector3.right * WaterThrust * fdt * (inputs.forward * 0.25f), ForceMode.VelocityChange);
                }
                else
                {
                    rigidbody.AddRelativeForce(0, WaterThrust * fdt, 0, ForceMode.VelocityChange);
                }

                if (inputs.sideways != 0)
                {
                    rigidbody.AddRelativeTorque(Vector3.up * WaterThrust * fdt * (inputs.sideways * 0.25f), ForceMode.VelocityChange);
                }
            }

            if (inputs.rocketBoosting)
            {
                rigidbody.AddRelativeForce(RocketThrust * fdt, ForceMode.VelocityChange);
            }

            var colProvider = OceanRenderer.Instance.CollisionProvider;
            var position = rigidbody.transform.position;

            var undispPos = Vector3.zero;
            if (!colProvider.ComputeUndisplacedPosition(ref position, ref undispPos)) return;

            if (!colProvider.SampleDisplacement(ref undispPos, ref _displacementToBoat)) return;
            if (!_displacementToBoatInitd)
            {
                _displacementToBoatLastFrame = _displacementToBoat;
                _displacementToBoatInitd = true;
            }

            // estimate water velocity
            Vector3 velWater = (_displacementToBoat - _displacementToBoatLastFrame) / dt; //Time.deltaTime;
            _displacementToBoatLastFrame = _displacementToBoat;

            var normal = Vector3.zero;
            if (!colProvider.SampleNormal(ref undispPos, ref normal, _boatWidth)) return;
            //Debug.DrawLine(rigidbody.transform.position, rigidbody.transform.position + 5f * normal);

            
            VelocityRelativeToWater = rigidbody.velocity - velWater;

            var dispPos = undispPos + _displacementToBoat;
            float height = dispPos.y;

            float bottomDepth = height - rigidbody.transform.position.y - _bottomH;

            InWater = bottomDepth > 0f;

            if (!InWater)
            {
                return;
            }

            var buoyancy = -Physics.gravity.normalized * _buoyancyCoeff * bottomDepth * bottomDepth * bottomDepth;
            rigidbody.AddForce(buoyancy * fdt, ForceMode.VelocityChange);


            // apply drag relative to water
            var forcePosition = rigidbody.transform.position + _forceHeightOffset * Vector3.up;
            rigidbody.AddForceAtPosition(Vector3.up * Vector3.Dot(Vector3.up, -VelocityRelativeToWater) * _dragInWaterUp * fdt, forcePosition, ForceMode.VelocityChange);
            rigidbody.AddForceAtPosition(rigidbody.transform.right * Vector3.Dot(rigidbody.transform.right, -VelocityRelativeToWater) * _dragInWaterRight * fdt, forcePosition, ForceMode.VelocityChange);
            rigidbody.AddForceAtPosition(rigidbody.transform.forward * Vector3.Dot(rigidbody.transform.forward, -VelocityRelativeToWater) * _dragInWaterForward * fdt, forcePosition, ForceMode.VelocityChange);
            rigidbody.AddForceAtPosition(rigidbody.transform.forward * _enginePower * inputs.forward * fdt, forcePosition, ForceMode.VelocityChange);
            rigidbody.AddTorque(rigidbody.transform.up * _turnPower * inputs.sideways * fdt, ForceMode.VelocityChange);

            //Debug.DrawLine(rigidbody.transform.position + Vector3.up * 5f, rigidbody.transform.position + 5f * (Vector3.up + rigidbody.transform.forward));

            // align to normal
            var current = rigidbody.transform.up;
            var target = normal;
            var torque = Vector3.Cross(current, target);
            rigidbody.AddTorque(torque * _boyancyTorque * fdt, ForceMode.VelocityChange);
        }

        private bool ClientHasStateMessage()
        {
            return this.client_state_msgs.Count > 0 && Time.time >= this.client_state_msgs.Peek().delivery_time;
        }

        private void ClientStoreCurrentStateAndStep(ref ClientState current_state, Rigidbody rigidbody, Inputs inputs, float fdt, float dt)
        {
            current_state.position = rigidbody.transform.position;
            current_state.rotation = rigidbody.transform.rotation;

            this.PrePhysicsStep(rigidbody, inputs, fdt, dt);
            //Physics.SyncTransforms();
            Physics.Simulate(fdt);
        }

        /*System.Collections.IEnumerator PingUpdate(string ip)
        {
            RestartLoop:
            var ping = new Ping(ip);

            yield return new WaitForSeconds(1f);
            while (!ping.isDone) yield return null;

            Debug.Log(ping.time);
            _pingTime.Add(ping.time);

            goto RestartLoop;
        }

        System.Collections.IEnumerator DoTheShit(uint rewind_tick_number, float dt)
        {
            while (rewind_tick_number < client_tick_number)
            {
                var buffer_slot = rewind_tick_number % c_client_buffer_size;
                this.ClientStoreCurrentStateAndStep(
                    ref this.client_state_buffer[buffer_slot],
                    client_proxy_rigidbody,
                    this.client_input_buffer[buffer_slot],
                    dt);

                ++rewind_tick_number;
                yield return null;
            }
        }*/

        internal static void AddClient(IPEndPoint ipEndpoint)
        {
            if (instance.clientList.Contains(ipEndpoint) == false)
            { // If it's a new client, add to the client list
                print($"Connect to {ipEndpoint}");
                instance.clientList.Add(ipEndpoint);
            }
        }

        /// <summary>
        /// TODO: We need to add timestamps to timeout and remove clients from the list.
        /// </summary>
        internal static void RemoveClient(IPEndPoint ipEndpoint)
        {
            instance.clientList.Remove(ipEndpoint);
        }

        /*#region API
        public void Send(string message)
        {
            if (isServer)
            {
                messageToDisplay += message + Environment.NewLine;
            }

            BroadcastChatMessage(message);
        }

        internal static void BroadcastChatMessage(string message)
        {
            foreach (var ip in instance.clientList)
            {
                instance.connection.Send(message, ip);
            }
        }
        #endregion*/
    }
}