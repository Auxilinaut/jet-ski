using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace JetSki{
	
	public class Scorer : MonoBehaviour {

		public ushort touchedLastId = 0;
		public bool somebodyScored = false;

		void OnCollisionEnter(Collision collision)
		{
			string other = collision.gameObject.name;

			if(other == "GoalRed")
			{
				Debug.Log("Blue team scored.");
				somebodyScored = true;
			}
			else if (other == "GoalBlue")
			{
				Debug.Log("Red team scored.");
				somebodyScored = true;
			}
			else if(!somebodyScored)
			{
				ushort idTouched = System.Convert.ToUInt16(other);
				if (idTouched < 9001) touchedLastId = idTouched;
			}
		}

	}

}