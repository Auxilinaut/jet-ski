// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

using UnityEngine;
using Crest;

public class BallAlignNormal : MonoBehaviour
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
    public Vector3 WaterThrust = new Vector3(0, -100000f, 0);

    void Start()
    {
        _rb = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
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

        float forward = _throttleBias;
        float sideways = _steerBias;

        if (_playerControlled)
        {
            forward += Input.GetAxis("Vertical");
            sideways += Input.GetAxis("Horizontal");
            RocketBoosting = (Input.GetMouseButton(0)) | (Input.GetButton("Fire2"));
            WaterBoosting = (Input.GetMouseButton(1)) | (Input.GetButton("Fire1")); //left click
            if (RocketBoosting)
            {
                _rb.AddRelativeForce(Vector3.Scale(Vector3.forward, RocketThrust), ForceMode.Acceleration);
                //_rb.AddRelativeForce((Vector3.Scale(Vector3.right, RocketThrust) * (sideways * 0.5f)), ForceMode.Acceleration);
            }
            if (WaterBoosting)
            {

                if (forward != 0)
                {
                    _rb.AddRelativeForce(Vector3.Scale(Vector3.up, WaterThrust), ForceMode.Acceleration);
                    _rb.AddRelativeForce(Vector3.Scale(Vector3.forward * forward, WaterThrust), ForceMode.Acceleration);
                }
                else
                {
                    _rb.AddRelativeForce(Vector3.Scale(Vector3.up, WaterThrust), ForceMode.Acceleration);
                }

                if (sideways != 0) _rb.AddRelativeTorque(Vector3.Scale(Vector3.up, WaterThrust) * (sideways * 0.5f), ForceMode.Acceleration);
            }
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

        _rb.AddForceAtPosition(transform.forward * _enginePower * forward, forcePosition, ForceMode.Acceleration);
        _rb.AddTorque(transform.up * _turnPower * sideways, ForceMode.Acceleration);

        //Debug.DrawLine(transform.position + Vector3.up * 5f, transform.position + 5f * (Vector3.up + transform.forward));

        // align to normal
        var current = transform.up;
        var target = normal;
        var torque = Vector3.Cross(current, target);
        _rb.AddTorque(torque * _boyancyTorque, ForceMode.Acceleration);
    }
}
