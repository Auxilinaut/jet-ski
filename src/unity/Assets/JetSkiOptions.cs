using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace JetSki{

	public class JetSkiOptions : MonoBehaviour {

		public bool rocketBoosting;
		public bool waterBoosting;
		//public GameObject rocketSystem;
		//public GameObject waterSystem;
		private GameObject rObj;
		private GameObject wObj;
		private ParticleSystem rSystem;
		private ParticleSystem wSystem;
		public ParticleSystem.EmissionModule rocketEmission;
		public ParticleSystem.EmissionModule waterEmission;
		private bool initializedStuff = false;

		public void InitializeStuff () {
			rocketBoosting = true;
			waterBoosting = true;

			rObj = Instantiate(Resources.Load("JetFire"), this.transform, false) as GameObject;//this.transform.Find("JetFire").gameObject.GetComponent<ParticleSystem>();
			wObj = Instantiate(Resources.Load("JetWater"), this.transform, false) as GameObject;//this.transform.Find("JetWater").gameObject.GetComponent<ParticleSystem>();

			rSystem = rObj.GetComponent<ParticleSystem>();
			rSystem.Play();

			wSystem = wObj.GetComponent<ParticleSystem>();
			wSystem.Play();

			rocketEmission = rSystem.emission;
			waterEmission = wSystem.emission;

			initializedStuff = true;
		}
		
		void Update () {
			if(initializedStuff)
			{
				rocketEmission.enabled = rocketBoosting;
				waterEmission.enabled = waterBoosting;
			}
			else
			{
				InitializeStuff();
			}
		}

	}
	
}