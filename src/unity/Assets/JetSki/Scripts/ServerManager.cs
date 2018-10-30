using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.IO;
using Google.Protobuf;
using JetSkiProto;
using static JetSkiProto.InputMessage.Types;

public class ServerManager : MonoBehaviour 
{
	public GameObject server_player_prefab;
	private Rigidbody[] server_rb;
	public uint server_snapshot_rate = 0; //64hz;
	private uint server_tick_number;
	private uint server_tick_accumulator;
	private Queue<InputMessage>[] server_input_msgs;

	public static ServerManager instance;

	void Start ()
	{
		instance = this;
	}
	
	void Update ()
	{
		uint server_tick_number = this.server_tick_number;
		uint server_tick_accumulator = this.server_tick_accumulator;
		// Rigidbody server_rigidbody = this.server_player.GetComponent<Rigidbody>();

		//***RECEIVE THE STUFF (UNTESTED)***
		/*if (theData != null)
		{
			Debug.Log("Received input message.");
			InputMessage input_msg = InputMessage.Parser.ParseFrom(theData);
			this.server_input_msgs.Enqueue();
			theData = null;
		}*/

		if (this.server_input_msgs.Count > 0)
		{
			InputMessage input_msg = this.server_input_msgs.Dequeue();

			PrePhysicsStep(_rb, input_msg.Inputs[0], fdt, Time.deltaTime);
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

	internal static void HandleData(byte[] data, IPEndPoint ipEndpoint)
	{
		if (Globals.gameOn)
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
					Globals.gameOn = false;
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
					globals.gameOn = true;
				break;
			}
		}
	}
}
