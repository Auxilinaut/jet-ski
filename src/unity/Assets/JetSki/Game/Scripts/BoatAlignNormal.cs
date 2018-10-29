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
using Google.Protobuf;
using JetSkiProto;
using static JetSkiProto.InputMessage.Types;
//using static JetSkiProto.InputMessage.Types;
//using static JetSkiProto.StateMessage.Types;

namespace JetSki
{
    public class BoatAlignNormal : MonoBehaviour
    {
        public struct ClientState
        {
            public Vector3 position;
            public Quaternion rotation;
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

        public GameObject other_player_prefab;
        public GameObject[] other_player;
        private Rigidbody[] other_player_rigidbody;

        public bool client_enable_corrections = true;
        public bool client_correction_smoothing = false;
        public bool client_send_redundant_inputs = false;
        private float client_timer;
        private uint client_tick_number;
        private uint client_last_received_state_tick;
        private const int c_client_buffer_size = 32; //1024;
        private ClientState[] client_state_buffer; // client stores predicted moves here
        private Inputs[] client_input_buffer; // client stores predicted inputs here
        private Queue<StateMessage> client_state_msgs;
        private Queue<StateMessage>[] other_player_state_msgs;
        private Vector3 client_pos_error;
        private Quaternion client_rot_error;

        // server specific
        //public GameObject server_player;
        Rigidbody[] _rb;
        public uint server_snapshot_rate = 0; //64hz;
        private uint server_tick_number;
        private uint server_tick_accumulator;
        private Queue<InputMessage>[] server_input_msgs;

        // networking 
        private string IPconnect = "127.0.0.1";
        private List<int> _pingTime = new List<int>();
        public float latency = 0f;

        public bool isClient;

        #region Data
        public static BoatAlignNormal instance;

        /// <summary>
        /// IP for clients to connect to. Null if you are the server.
        /// </summary>
        public IPAddress serverIp;

        /// <summary>
        /// For Clients, there is only one and it's the connection to the server.
        /// For Servers, there are many - one per connected client.
        /// </summary>
        public List<IPEndPoint> clientList = new List<IPEndPoint>();

        public static byte[] theData;

        UdpConnectedClient connection;
        #endregion

        public bool gameOn = false;

        public void Awake()
        {
            Application.targetFrameRate = 60;
            instance = this;

            if (isClient)
            {
                this.isClient = true;
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
            /*foreach( var ldaw in OceanRenderer.Instance._lodDataAnimWaves)
            {
                readbackShape = readbackShape && ldaw._readbackShapeForCollision;
            }*/
            //this.StartCoroutine(PingUpdate(IPconnect));
            if (!isClient)
            {
                //IPconnect = "127.0.0.1";
                //this.StartCoroutine(PingUpdate(IPconnect));
                //_rb = this.server_player.GetComponent<Rigidbody>();
                for (int i = 0; i<_rb.Length; i++)
                {
                    _rb = GetComponent<Rigidbody>();
                }
                

                this.server_tick_number = 0;
                this.server_tick_accumulator = 0;
                //this.server_input_msgs = new Queue<InputMessage>();

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
            float fdt = Time.fixedDeltaTime;

            if (isClient) /* ************CLIENT UPDATE************ */
            {
                float client_timer = this.client_timer;
                uint client_tick_number = this.client_tick_number;
                client_timer += Time.deltaTime;

                CameraLockedOnBall = Input.GetKeyDown(KeyCode.Space) | Input.GetButtonDown("Jump");

                Inputs inputs = new Inputs{
                    Forward = 0 + Input.GetAxis("Vertical"),
                    Sideways = 0 + Input.GetAxis("Horizontal"),
                    RocketBoosting = Input.GetMouseButton(0) | Input.GetButton("Fire2"),
                    WaterBoosting = Input.GetMouseButton(1) | Input.GetButton("Fire1")
                };

                //***SEND THE STUFF (UNTESTED)***
                Debug.Log("Sending input_msg");
                InputMessage input_msg = new InputMessage{
                    DeliveryTime = Time.time + this.latency,// + 0.1f;//this._pingTime[this._pingTime.Count - 1],
                    StartTickNumber = this.client_send_redundant_inputs ? this.client_last_received_state_tick : client_tick_number,
                    Inputs = {inputs}
                };

                connection.Send(input_msg.ToByteArray(), clientList[0]);

                ++client_tick_number;

                /*while (client_timer >= fdt)
                {
                    client_timer -= fdt;

                    uint buffer_slot = client_tick_number % c_client_buffer_size;

                    Inputs inputs = new Inputs{
                        Forward = 0 + Input.GetAxis("Vertical"),
                        Sideways = 0 + Input.GetAxis("Horizontal"),
                        RocketBoosting = Input.GetMouseButton(0) | Input.GetButton("Fire2"),
                        WaterBoosting = Input.GetMouseButton(1) | Input.GetButton("Fire1")
                    };
                    
                    this.client_input_buffer[buffer_slot] = inputs;

                    // store state for this tick, then use current state + input to step simulation
                    this.ClientStoreCurrentStateAndStep(
                        ref this.client_state_buffer[buffer_slot],
                        client_proxy_rigidbody,
                        inputs,
                        fdt, client_timer);

                    //***SEND THE STUFF (UNTESTED)***
                    Debug.Log("Sending input_msg");
                    InputMessage input_msg = new InputMessage{
                        DeliveryTime = Time.time + this.latency,// + 0.1f;//this._pingTime[this._pingTime.Count - 1],
                        StartTickNumber = this.client_send_redundant_inputs ? this.client_last_received_state_tick : client_tick_number,
                        Inputs = {new Inputs ()}
                    };

                    input_msg.Inputs.Add(inputs);

                    for (uint tick = input_msg.StartTickNumber; tick <= client_tick_number; ++tick)
                    {
                        input_msg.Inputs.Add(this.client_input_buffer[tick % c_client_buffer_size]);
                    }

                    connection.Send(input_msg.ToByteArray(), clientList[0]);

                    ++client_tick_number;
                }*/

                //***RECEIVE THE STUFF (UNTESTED)***
                if (theData != null)
                {
                    Debug.Log("Received state message");
                    this.client_state_msgs.Enqueue(StateMessage.Parser.ParseFrom(theData));
                    theData = null;
                }

                if (this.ClientHasStateMessage())
                {
                    StateMessage state_msg = this.client_state_msgs.Dequeue();
                    /*while (this.ClientHasStateMessage()) // make sure if there are any newer state messages available, we use those instead
                    {
                        state_msg = this.client_state_msgs.Dequeue();
                    }*/

                    this.client_last_received_state_tick = state_msg.TickNumber;

                    client_proxy_rigidbody.transform.SetPositionAndRotation(state_msg.Position, state_msg.Rotation);
                    //client_proxy_rigidbody.transform.position = state_msg.Position;
                    //client_proxy_rigidbody.transform.rotation = state_msg.Rotation;



                    /*if (this.client_enable_corrections)
                    {
                        uint buffer_slot = state_msg.TickNumber % c_client_buffer_size;
                        Vector3 position_error = state_msg.Position - this.client_state_buffer[buffer_slot].position;
                        float rotation_error = 1.0f - Quaternion.Dot(state_msg.Rotation, this.client_state_buffer[buffer_slot].rotation);

                        if (position_error.sqrMagnitude > 0.0000001f || rotation_error > 0.00001f)
                        {
                            // capture the current predicted pos for smoothing
                            Vector3 prev_pos = client_proxy_rigidbody.transform.position + this.client_pos_error;
                            Quaternion prev_rot = client_proxy_rigidbody.transform.rotation * this.client_rot_error;

                            // rewind & replay
                            client_proxy_rigidbody.transform.position = state_msg.Position;
                            client_proxy_rigidbody.transform.rotation = state_msg.Rotation;
                            client_proxy_rigidbody.velocity = state_msg.Velocity;
                            client_proxy_rigidbody.angularVelocity = state_msg.AngularVelocity;

                            uint rewind_tick_number = state_msg.TickNumber;
                            //StartCoroutine(DoTheShit(rewind_tick_number,dt));
                            while (rewind_tick_number < client_tick_number)
                            {
                                buffer_slot = rewind_tick_number % c_client_buffer_size;
                                this.ClientStoreCurrentStateAndStep(
                                    ref this.client_state_buffer[buffer_slot],
                                    client_proxy_rigidbody,
                                    this.client_input_buffer[buffer_slot],
                                    fdt, client_timer);

                                ++rewind_tick_number;
                            }

                            // if more than 2ms apart, just snap
                            if ((prev_pos - client_proxy_rigidbody.transform.position).sqrMagnitude > 4.0f)//.sqrMagnitude >= 4.0f)
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
                    }*/

                }
                else
                {
                    Debug.Log("No state message.");
                }

                this.client_timer = client_timer;
                this.client_tick_number = client_tick_number;

                /*if (this.client_correction_smoothing)
                {
                    this.client_pos_error *= 0.9f;
                    this.client_rot_error = Quaternion.Slerp(this.client_rot_error, Quaternion.identity, 0.1f);
                }
                else
                {
                    this.client_pos_error = Vector3.zero;
                    this.client_rot_error = Quaternion.identity;
                }*/
                
                client_player.transform.SetPositionAndRotation(client_proxy_rigidbody.transform.position, client_proxy_rigidbody.transform.rotation);
                //client_player.transform.position = client_proxy_rigidbody.transform.position;// + this.client_pos_error;
                //client_player.transform.rotation = client_proxy_rigidbody.transform.rotation;// * this.client_rot_error;
            }
            else /* ************SERVER UPDATE************ */
            {
                uint server_tick_number = this.server_tick_number;
                uint server_tick_accumulator = this.server_tick_accumulator;
                // Rigidbody server_rigidbody = this.server_player.GetComponent<Rigidbody>();

                //***RECEIVE THE STUFF (UNTESTED)***
                if (theData != null)
                {
                    Debug.Log("Received input message.");
                    InputMessage input_msg = InputMessage.Parser.ParseFrom(theData);
                    this.server_input_msgs.Enqueue();
                    theData = null;
                }

                if (this.server_input_msgs.Count > 0)
                {
                    InputMessage input_msg = this.server_input_msgs.Dequeue();

                    this.PrePhysicsStep(_rb, input_msg.Inputs[0], fdt, Time.deltaTime);
                    Physics.Simulate(fdt);

                    StateMessage state_msg = new StateMessage{
                        DeliveryTime = Time.time + this.latency,// + 0.1f;//+ this._pingTime[this._pingTime.Count-1];
                        TickNumber = server_tick_number,
                        Position = _rb.transform.position,
                        Rotation = _rb.transform.rotation,
                        Velocity = _rb.velocity,
                        AngularVelocity = _rb.angularVelocity
                    };

                    //***SEND THE STUFF (UNTESTED)***
                    Debug.Log("Sending state message.");
                    connection.Send(state_msg.ToByteArray(), clientList[0]);
                }
                else
                {
                    Debug.Log("No input messages.");
                }
                /*while (this.server_input_msgs.Count > 0 && Time.time >= this.server_input_msgs.Peek().DeliveryTime)
                {
                    InputMessage input_msg = this.server_input_msgs.Dequeue();

                    // message contains an array of inputs, calculate what tick the final one is
                    uint max_tick = input_msg.StartTickNumber + (uint)input_msg.Inputs.Count - 1;

                    // if that tick is greater than or equal to the current tick we're on, then it
                    // has inputs which are new
                    if (max_tick >= server_tick_number)
                    {
                        // there may be some inputs in the array that we've already had,
                        // so figure out where to start
                        uint start_i = server_tick_number > input_msg.StartTickNumber ? (server_tick_number - input_msg.StartTickNumber) : 0;

                        // run through all relevant inputs, and step player forward
                        for (int i = (int)start_i; i < input_msg.Inputs.Count; ++i)
                        {
                            this.PrePhysicsStep(_rb, input_msg.Inputs[i], fdt, Time.deltaTime);
                            //Physics.SyncTransforms();
                            Physics.Simulate(fdt);

                            ++server_tick_accumulator;
                            if (server_tick_accumulator >= this.server_snapshot_rate)
                            {
                                server_tick_accumulator = 0;

                                StateMessage state_msg = new StateMessage{
                                    DeliveryTime = Time.time + this.latency,// + 0.1f;//+ this._pingTime[this._pingTime.Count-1];
                                    TickNumber = server_tick_number,
                                    Position = _rb.transform.position,
                                    Rotation = _rb.transform.rotation,
                                    Velocity = _rb.velocity,
                                    AngularVelocity = _rb.angularVelocity
                                };

                                //***SEND THE STUFF (UNTESTED)***
                                connection.Send(state_msg.ToByteArray(), clientList[0]);
                            }
                        }

                        //this.server_display_player.rigidbody.transform.position = server_rigidbody.position;
                        //this.server_display_player.rigidbody.transform.rotation = server_rigidbody.rotation;

                        server_tick_number = max_tick + 1;
                    }
                }*/

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
            if (inputs.WaterBoosting)
            {
                if (inputs.Forward != 0)
                {
                    rigidbody.AddRelativeForce(0, WaterThrust * fdt, 0, ForceMode.VelocityChange);
                    rigidbody.AddRelativeForce(0, 0, WaterThrust * fdt * inputs.Forward, ForceMode.VelocityChange);
                    rigidbody.AddRelativeTorque(Vector3.right * WaterThrust * fdt * (inputs.Forward * 0.25f), ForceMode.VelocityChange);
                }
                else
                {
                    rigidbody.AddRelativeForce(0, WaterThrust * fdt, 0, ForceMode.VelocityChange);
                }

                if (inputs.Sideways != 0)
                {
                    rigidbody.AddRelativeTorque(Vector3.up * WaterThrust * fdt * (inputs.Sideways * 0.25f), ForceMode.VelocityChange);
                }
            }

            if (inputs.RocketBoosting)
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
            Vector3 velWater = (_displacementToBoat - _displacementToBoatLastFrame) / fdt; //Time.deltaTime;
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
            rigidbody.AddForceAtPosition(rigidbody.transform.forward * _enginePower * inputs.Forward * fdt, forcePosition, ForceMode.VelocityChange);
            rigidbody.AddTorque(rigidbody.transform.up * _turnPower * inputs.Sideways * fdt, ForceMode.VelocityChange);

            //Debug.DrawLine(rigidbody.transform.position + Vector3.up * 5f, rigidbody.transform.position + 5f * (Vector3.up + rigidbody.transform.forward));

            // align to normal
            var current = rigidbody.transform.up;
            var target = normal;
            var torque = Vector3.Cross(current, target);
            rigidbody.AddTorque(torque * _boyancyTorque * fdt, ForceMode.VelocityChange);
        }

        private bool ClientHasStateMessage()
        {
            return this.client_state_msgs.Count > 0;// && Time.time >= this.client_state_msgs.Peek().DeliveryTime;
        }

        /*private void ClientStoreCurrentStateAndStep(ref ClientState current_state, Rigidbody rigidbody, Inputs inputs, float fdt, float dt)
        {
            current_state.position = rigidbody.transform.position;
            current_state.rotation = rigidbody.transform.rotation;

            this.PrePhysicsStep(rigidbody, inputs, fdt, dt);
            //Physics.SyncTransforms();
            Physics.Simulate(fdt);
        }

        System.Collections.IEnumerator PingUpdate(string ip)
        {
            RestartLoop:
            var ping = new Ping(ip);

            yield return new WaitForSeconds(1f);
            while (!ping.isDone) yield return null;

            Debug.Log(ping.time);
            _pingTime.Add(ping.time);

            goto RestartLoop;
        }

        System.Collections.IEnumerator DoTheShit(uint rewind_tick_number, float fdt, float dt)
        {
            while (rewind_tick_number < client_tick_number)
            {
                var buffer_slot = rewind_tick_number % c_client_buffer_size;
                this.ClientStoreCurrentStateAndStep(
                    ref this.client_state_buffer[buffer_slot],
                    client_proxy_rigidbody,
                    this.client_input_buffer[buffer_slot],
                    fdt, dt);

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
        }*/

        /*internal static void BroadcastChatMessage(string message)
        {
            foreach (var ip in instance.clientList)
            {
                instance.connection.Send(message, ip);
            }
        }*/

        internal static void HandleData(byte[] data, IPEndPoint ipEndpoint)
        {
            if (instance.isClient) //Client Data Handler
            {
                if (instance.gameOn)
                {
                    GameOnMessage msg = GameOnMessage.Parser.ParseFrom(data);
                    Debug.Log("GameOnMessage: " + msg.ToString());
                    switch ((int)msg.GameOnCase)
                    {
                        case 2: //*****GET STATE MESSAGE (UNIMPLEMENTED)*****
                            Debug.Log("ID: " + msg.ServerStateMsg.Id);
                        break;

                        case 3: //*****SCORE UDPATE (UNIMPLEMENTED)*****
                            Debug.Log("Somebody scored.");
                            Debug.Log("ID: " + msg.ScoreMsg.Id);
                            Debug.Log("Score: " + msg.ScoreMsg.Score);
                        break;

                        case 4: //*****STOP GAME (UNTESTED)*****
                            instance.gameOn = false;
                        break;
                    }
                }
                else
                {
                    GameOffMessage msg = GameOffMessage.Parser.ParseFrom(data);
                    Debug.Log("GameOffMessage: " + msg.ToString());
                    switch ((int)msg.GameOffCase)
                    {
                        case 1: //*****JOIN SERVER (UNIMPLEMENTED)*****
                            Debug.Log("Joining server.");
                            Debug.Log("Team: " + msg.AcceptJoinMsg.Team);
                            Debug.Log("Position: " + msg.AcceptJoinMsg.Position);
                            Debug.Log("Rotation: " + msg.AcceptJoinMsg.Rotation);
                            instance.JoinServer(msg.AcceptJoinMsg.Team, msg.AcceptJoinMsg.Position, msg.AcceptJoinMsg.Rotation);
                        break;

                        case 2: //*****NEW OTHER PLAYERS (UNIMPLEMENTED)*****
                            Debug.Log("New player joined.");
                            Debug.Log("ID: " + msg.NewPlayerMsg.Id);
                            Debug.Log("Team: " + msg.NewPlayerMsg.Team);
                            Debug.Log("Position: " + msg.NewPlayerMsg.Position);
                            Debug.Log("Rotation: " + msg.NewPlayerMsg.Rotation);
                            instance.NewOtherPlayer(msg.NewPlayerMsg.Id, msg.NewPlayerMsg.Team, msg.NewPlayerMsg.Position, msg.NewPlayerMsg.Rotation);
                        break;

                        case 3: //*****START GAME (UNTESTED)*****
                            instance.gameOn = true;
                        break;
                    }
                }
            }
            else //Server Data Handler
            {
                if (instance.gameOn)
                {
                    GameOnMessage msg = GameOnMessage.Parser.ParseFrom(data);
                    Debug.Log("GameOnMessage: " + msg.ToString());
                    switch ((int)msg.GameOnCase)
                    {
                        case 1: //*****GET INPUT MESSAGE (UNIMPLEMENTED)*****
                            Debug.Log("ID: " + msg.ClientInputMsg.Id);
                        break;

                        case 2: //*****SCORE UDPATE (UNIMPLEMENTED)*****
                            Debug.Log("Somebody scored.");
                            Debug.Log("ID: " + msg.ScoreMsg.Id);
                            Debug.Log("Score: " + msg.ScoreMsg.Score);
                        break;

                        case 3: //*****STOP GAME (UNTESTED)*****
                            instance.gameOn = false;
                        break;
                    }
                }
                else
                {
                    GameOffMessage msg = GameOffMessage.Parser.ParseFrom(data);
                    Debug.Log("GameOffMessage: " + msg.ToString());
                    switch ((int)msg.GameOffCase)
                    {
                        case 1: //*****JOIN SERVER (UNIMPLEMENTED)*****
                            Debug.Log("Joining server.");
                            Debug.Log("Team: " + msg.AcceptJoinMsg.Team);
                            Debug.Log("Position: " + msg.AcceptJoinMsg.Position);
                            Debug.Log("Rotation: " + msg.AcceptJoinMsg.Rotation);
                            instance.JoinServer(msg.AcceptJoinMsg.Team, msg.AcceptJoinMsg.Position, msg.AcceptJoinMsg.Rotation);
                        break;

                        case 2: //*****NEW OTHER PLAYERS (UNIMPLEMENTED)*****
                            Debug.Log("New player joined.");
                            Debug.Log("ID: " + msg.NewPlayerMsg.Id);
                            Debug.Log("Team: " + msg.NewPlayerMsg.Team);
                            Debug.Log("Position: " + msg.NewPlayerMsg.Position);
                            Debug.Log("Rotation: " + msg.NewPlayerMsg.Rotation);
                            instance.NewOtherPlayer(msg.NewPlayerMsg.Id, msg.NewPlayerMsg.Team, msg.NewPlayerMsg.Position, msg.NewPlayerMsg.Rotation);
                        break;

                        case 3: //*****START GAME (UNTESTED)*****
                            instance.gameOn = true;
                        break;
                    }
                }
            }
        }
        //#endregion

        //CLIENT MESSAGE HANDLERS GAMEOFF

        private void JoinServer(uint team, Vector3 position, Quaternion rotation)
        {

        }

        private void NewOtherPlayer(string id, uint team, Vector3 position, Quaternion rotation)
        {

        }

        //SERVER MESSAGE HANDLERS GAMEON

        private void ReceiveInputs()
        {
            
        }

    }
}