// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

using System;
using System.Collections.Generic;
using UnityEngine;
using Crest;
using System.Net;
using System.Net.Sockets;
using System.IO;
using Google.Protobuf;
using JetSkiProto;
using static JetSkiProto.InputMessage.Types;
//using static JetSkiProto.StateMessage.Types;

namespace JetSki
{
    public class BoatAlignNormal : MonoBehaviour
    {
        

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

        public static BoatAlignNormal instance;

        public void Awake()
        {
            instance = this;
        }

        void Start()
        {
            /*foreach( var ldaw in OceanRenderer.Instance._lodDataAnimWaves)
            {
                readbackShape = readbackShape && ldaw._readbackShapeForCollision;
            }*/
            //this.StartCoroutine(PingUpdate(IPconnect));
            //IPconnect = "127.0.0.1";
            //this.StartCoroutine(PingUpdate(IPconnect));
            
        }

        private void Update()
        {
            
        }

        public void PrePhysicsStep(Rigidbody rigidbody, Inputs inputs, float fdt, float dt)
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
    }
}