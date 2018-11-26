using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class JetSkiOptions : MonoBehaviour {

	public bool rocketBoosting = false;
	public bool waterBoosting = false;
	public ParticleSystem rocketSystem;
	public ParticleSystem waterSystem;
	ParticleSystem.EmissionModule rocketEmission;
	ParticleSystem.EmissionModule waterEmission;


	// Use this for initialization
	void Start () {
		rocketEmission = rocketSystem.emission;
		waterEmission = waterSystem.emission;
	}
	
	// Update is called once per frame
	void Update () {
		rocketEmission.enabled = rocketBoosting;
		waterEmission.enabled = waterBoosting;
	}
}
