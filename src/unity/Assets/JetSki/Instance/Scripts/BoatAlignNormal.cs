// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

using UnityEngine;
using Crest;
using System.Collections.Generic;

public class BoatAlignNormal : MonoBehaviour
{
    public struct Inputs
    {
        public float forward;
        public float sideways;
        public bool waterBoosting;
        public bool rocketBoosting;
    }

    public struct InputMessage
    {
        public float delivery_time;
        public uint start_tick_number;
        public List<Inputs> inputs;
    }

    public struct ClientState
    {
        public Vector3 position;
        public Quaternion rotation;
    }

    public struct StateMessage
    {
        public float delivery_time;
        public uint tick_number;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 velocity;
        public Vector3 angular_velocity;
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

    Rigidbody _rb;

    public float _dragInWaterUp = 20000f;
    public float _dragInWaterRight = 20000f;
    public float _dragInWaterForward = 20000f;

    bool _inWater;
    public bool InWater { get { return _inWater; } }

    Vector3 _velocityRelativeToWater;
    public Vector3 VelocityRelativeToWater { get { return _velocityRelativeToWater; } }

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
    public bool client_enable_corrections = true;
    public bool client_correction_smoothing = true;
    public bool client_send_redundant_inputs = true;
    private float client_timer;
    private uint client_tick_number;
    private uint client_last_received_state_tick;
    private const int c_client_buffer_size = 1024;
    private ClientState[] client_state_buffer; // client stores predicted moves here
    private Inputs[] client_input_buffer; // client stores predicted inputs here
    private Queue<StateMessage> client_state_msgs;
    private Vector3 client_pos_error;
    private Quaternion client_rot_error;

    // server specific
    public GameObject server_player;
    public uint server_snapshot_rate;
    private uint server_tick_number;
    private uint server_tick_accumulator;
    private Queue<InputMessage> server_input_msgs;

    void Start()
    {
        _rb = GetComponent<Rigidbody>();
        this.client_timer = 0.0f;
        this.client_tick_number = 0;
        this.client_last_received_state_tick = 0;
        this.client_state_buffer = new ClientState[c_client_buffer_size];
        this.client_input_buffer = new Inputs[c_client_buffer_size];
        this.client_state_msgs = new Queue<StateMessage>();
        this.client_pos_error = Vector3.zero;
        this.client_rot_error = Quaternion.identity;
    }

    void Update()
    {
        float dt = Time.fixedDeltaTime;
        float client_timer = this.client_timer;
        uint client_tick_number = this.client_tick_number;
        client_timer += Time.deltaTime;

        //float forward = _throttleBias;
        //float sideways = _steerBias;

        if (_playerControlled)
        {
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
                RocketBoosting = Input.GetMouseButton(0) | Input.GetButton("Fire2");
                WaterBoosting = Input.GetMouseButton(1) | Input.GetButton("Fire1");
                this.client_input_buffer[buffer_slot] = inputs;

                // store state for this tick, then use current state + input to step simulation
                this.ClientStoreCurrentStateAndStep(
                    ref this.client_state_buffer[buffer_slot],
                    _rb,
                    inputs,
                    dt);

                // send input packet to server
                InputMessage input_msg;
                input_msg.delivery_time = Time.time + this.latency;
                input_msg.start_tick_number = this.client_send_redundant_inputs ? this.client_last_received_state_tick : client_tick_number;
                input_msg.inputs = new List<Inputs>();

                for (uint tick = input_msg.start_tick_number; tick <= client_tick_number; ++tick)
                {
                    input_msg.inputs.Add(this.client_input_buffer[tick % c_client_buffer_size]);
                }

                //***SEND THE STUFF (UNIMPLEMENTED)***

                //this.server_input_msgs.Enqueue(input_msg);

                ++client_tick_number;
            }

            if (this.ClientHasStateMessage())
            {
                StateMessage state_msg = this.client_state_msgs.Dequeue();
                while (this.ClientHasStateMessage()) // make sure if there are any newer state messages available, we use those instead
                {
                    state_msg = this.client_state_msgs.Dequeue();
                }

                this.client_last_received_state_tick = state_msg.tick_number;

                //this.proxy_player.transform.position = state_msg.position;
                //this.proxy_player.transform.rotation = state_msg.rotation;

                if (this.client_enable_corrections)
                {
                    uint buffer_slot = state_msg.tick_number % c_client_buffer_size;
                    Vector3 position_error = state_msg.position - this.client_state_buffer[buffer_slot].position;
                    float rotation_error = 1.0f - Quaternion.Dot(state_msg.rotation, this.client_state_buffer[buffer_slot].rotation);

                    if (position_error.sqrMagnitude > 0.0000001f ||
                        rotation_error > 0.00001f)
                    {
                        // capture the current predicted pos for smoothing
                        Vector3 prev_pos = _rb.position + this.client_pos_error;
                        Quaternion prev_rot = _rb.rotation * this.client_rot_error;

                        // rewind & replay
                        _rb.position = state_msg.position;
                        _rb.rotation = state_msg.rotation;
                        _rb.velocity = state_msg.velocity;
                        _rb.angularVelocity = state_msg.angular_velocity;

                        uint rewind_tick_number = state_msg.tick_number;
                        while (rewind_tick_number < client_tick_number)
                        {
                            buffer_slot = rewind_tick_number % c_client_buffer_size;
                            this.ClientStoreCurrentStateAndStep(
                                ref this.client_state_buffer[buffer_slot],
                                _rb,
                                this.client_input_buffer[buffer_slot],
                                dt);

                            ++rewind_tick_number;
                        }

                        // if more than 2ms apart, just snap
                        if ((prev_pos - _rb.position).sqrMagnitude >= 4.0f)
                        {
                            this.client_pos_error = Vector3.zero;
                            this.client_rot_error = Quaternion.identity;
                        }
                        else
                        {
                            this.client_pos_error = prev_pos - _rb.position;
                            this.client_rot_error = Quaternion.Inverse(_rb.rotation) * prev_rot;
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

            //this.smoothed_client_player.transform.position = client_rigidbody.position + this.client_pos_error;
            //this.smoothed_client_player.transform.rotation = client_rigidbody.rotation * this.client_rot_error;
        }
        else
        {
            uint server_tick_number = this.server_tick_number;
            uint server_tick_accumulator = this.server_tick_accumulator;
            Rigidbody server_rigidbody = this.server_player.GetComponent<Rigidbody>();

            //***RECEIVE STUFF (UNIMPLEMENTED)***

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
                        this.PrePhysicsStep(server_rigidbody, input_msg.inputs[i]);
                        Physics.Simulate(dt);

                        ++server_tick_accumulator;
                        if (server_tick_accumulator >= this.server_snapshot_rate)
                        {
                            server_tick_accumulator = 0;

                            StateMessage state_msg;
                            state_msg.delivery_time = Time.time + this.latency;
                            state_msg.tick_number = server_tick_number;
                            state_msg.position = server_rigidbody.position;
                            state_msg.rotation = server_rigidbody.rotation;
                            state_msg.velocity = server_rigidbody.velocity;
                            state_msg.angular_velocity = server_rigidbody.angularVelocity;

                            //***SEND THE STUFF (UNIMPLEMENTED)***
                            //this.client_state_msgs.Enqueue(state_msg);
                        }
                    }

                    //this.server_display_player.transform.position = server_rigidbody.position;
                    //this.server_display_player.transform.rotation = server_rigidbody.rotation;

                    server_tick_number = max_tick + 1;
                }
            }

            this.server_tick_number = server_tick_number;
            this.server_tick_accumulator = server_tick_accumulator;
        }
    }

    private void PrePhysicsStep(Rigidbody rigidbody, Inputs inputs)
    {
        var colProvider = OceanRenderer.Instance.CollisionProvider;
        var position = transform.position;

        var undispPos = Vector3.zero;
        if (!colProvider.ComputeUndisplacedPosition(ref position, ref undispPos)) return;

        if (!colProvider.SampleDisplacement(ref undispPos, ref _displacementToBoat)) return;
        if (!_displacementToBoatInitd)
        {
            _displacementToBoatLastFrame = _displacementToBoat;
            _displacementToBoatInitd = true;
        }

        // estimate water velocity
        Vector3 velWater = (_displacementToBoat - _displacementToBoatLastFrame) / Time.deltaTime;
        _displacementToBoatLastFrame = _displacementToBoat;

        var normal = Vector3.zero;
        if (!colProvider.SampleNormal(ref undispPos, ref normal, _boatWidth)) return;
        Debug.DrawLine(transform.position, transform.position + 5f * normal);

        _velocityRelativeToWater = _rb.velocity - velWater;

        var dispPos = undispPos + _displacementToBoat;
        float height = dispPos.y;

        float bottomDepth = height - transform.position.y - _bottomH;

        _inWater = bottomDepth > 0f;
        
        if (inputs.waterBoosting)
        {
            if (inputs.forward != 0)
            {
                _rb.AddRelativeForce(0, WaterThrust, 0, ForceMode.Acceleration);
                _rb.AddRelativeForce(0, 0, WaterThrust * inputs.forward, ForceMode.Acceleration);
                _rb.AddRelativeTorque(Vector3.right * WaterThrust * (inputs.forward * 0.25f), ForceMode.Acceleration);
            }
            else
            {
                _rb.AddRelativeForce(0, WaterThrust, 0, ForceMode.Acceleration);
            }

            if(inputs.sideways != 0)
            {
                _rb.AddRelativeTorque(Vector3.up * WaterThrust * (inputs.sideways * 0.25f), ForceMode.Acceleration);
                //rigidbody.AddForce(-this.local_player_camera_transform.forward * this.player_movement_impulse, ForceMode.Impulse);
            }
        }

        if (inputs.rocketBoosting)
        {
            _rb.AddRelativeForce(RocketThrust, ForceMode.Acceleration);
            //_rb.AddRelativeForce((Vector3.Scale(Vector3.right, RocketThrust) * (sideways * 0.5f)), ForceMode.Acceleration);
        }

        if (!_inWater)
        {
            return;
        }

        var buoyancy = -Physics.gravity.normalized * _buoyancyCoeff * bottomDepth * bottomDepth * bottomDepth;
        _rb.AddForce(buoyancy, ForceMode.Acceleration);


        // apply drag relative to water
        var forcePosition = _rb.position + _forceHeightOffset * Vector3.up;
        _rb.AddForceAtPosition(Vector3.up * Vector3.Dot(Vector3.up, -_velocityRelativeToWater) * _dragInWaterUp, forcePosition, ForceMode.Acceleration);
        _rb.AddForceAtPosition(transform.right * Vector3.Dot(transform.right, -_velocityRelativeToWater) * _dragInWaterRight, forcePosition, ForceMode.Acceleration);
        _rb.AddForceAtPosition(transform.forward * Vector3.Dot(transform.forward, -_velocityRelativeToWater) * _dragInWaterForward, forcePosition, ForceMode.Acceleration);

        _rb.AddForceAtPosition(transform.forward * _enginePower * inputs.forward, forcePosition, ForceMode.Acceleration);
        _rb.AddTorque(transform.up * _turnPower * inputs.sideways, ForceMode.Acceleration);

        //Debug.DrawLine(transform.position + Vector3.up * 5f, transform.position + 5f * (Vector3.up + transform.forward));

        // align to normal
        var current = transform.up;
        var target = normal;
        var torque = Vector3.Cross(current, target);
        _rb.AddTorque(torque * _boyancyTorque, ForceMode.Acceleration);
    }

    private bool ClientHasStateMessage()
    {
        return this.client_state_msgs.Count > 0 && Time.time >= this.client_state_msgs.Peek().delivery_time;
    }

    private void ClientStoreCurrentStateAndStep(ref ClientState current_state, Rigidbody rigidbody, Inputs inputs, float dt)
    {
        current_state.position = rigidbody.position;
        current_state.rotation = rigidbody.rotation;

        this.PrePhysicsStep(rigidbody, inputs);
        Physics.Simulate(dt);
    }
}
