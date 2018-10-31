using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.IO;
using Google.Protobuf;
using JetSkiProto;
using static JetSkiProto.InputMessage.Types;

namespace JetSki
{
	public class ServerManager : MonoBehaviour 
	{
		public GameObject server_player_prefab;
		public uint server_snapshot_rate = 0; //64hz;
		private uint server_tick_number;
		private uint server_tick_accumulator;
		private Rigidbody[] server_rb;
		private Queue<GameOffMessage>[] game_off_msgs;
		private Queue<InputMessage>[] server_input_msgs;

		public static ServerManager instance;
		private GameManager gameManager;

		void Start ()
		{
			instance = this;
			gameManager = GameManager.instance;
		}
		
		void Update ()
		{
			if (Globals.gameOn)
			{
				float fdt = Time.fixedDeltaTime;
				uint server_tick_number = this.server_tick_number;
				uint server_tick_accumulator = this.server_tick_accumulator;

				for (var i=0; i<gameManager.clientList.Count;i++)
				{
					if (this.server_input_msgs[i].Count > 0)
					{
						InputMessage input_msg = this.server_input_msgs[i].Dequeue();

						BoatAlignNormal.instance.PrePhysicsStep(server_rb[i], input_msg.Inputs[0], fdt, Time.deltaTime);
					}
					else
					{
						Debug.Log("No input messages.");
					}
				}

				Physics.Simulate(fdt);

				for (var i=0; i<gameManager.clientList.Count;i++)
				{
					StateMessage state_msg = new StateMessage{
						Id = gameManager.clientList[i],
						DeliveryTime = Time.time + gameManager.latency,// + 0.1f;//+ this._pingTime[this._pingTime.Count-1];
						TickNumber = server_tick_number,
						Position = server_rb[i].transform.position,
						Rotation = server_rb[i].transform.rotation,
						Velocity = server_rb[i].velocity,
						AngularVelocity = server_rb[i].angularVelocity
					};
					gameManager.connection.Send(state_msg.ToByteArray(),gameManager.clientList[i]);
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

		internal static void HandleData(byte[] data, IPEndPoint iPEndPoint)
		{
			if (Globals.gameOn)
			{
				GameOnMessage msg = GameOnMessage.Parser.ParseFrom(data);
				Debug.Log("GameOnMessage: " + msg.ToString());
				switch ((int)msg.GameOnCase)
				{
					case 1: //*****GET INPUT MESSAGE (UNIMPLEMENTED)*****
						Debug.Log("Input from client " + msg.ClientInputMsg.Id);
						instance.server_input_msgs[msg.ClientInputMsg.Id].Enqueue(msg.ClientInputMsg);
					break;

					case 5: //*****CLIENT ACK RECEIVED *****
						Debug.Log("Ack from client " + msg.AckMsg.Id);
					break;
				}
			}
			else
			{
				GameOffMessage msg = GameOffMessage.Parser.ParseFrom(data);
				Debug.Log("GameOffMessage: " + msg.ToString());
				switch ((int)msg.GameOffCase)
				{
					case 1:
						Debug.Log("Request to join from client " + msg.JoinServerMsg.Name);
						instance.gameManager.clientList.Add(iPEndPoint);
						//SEND TO THAT CLIENT THEIR STUFF
						//instance.gameManager.connection.Send();
					break;
					case 5: //*****ACK FROM CLIENT (UNIMPLEMENTED)*****
						Debug.Log("Ack from client " + msg.AckMsg.Id);
					break;
				}
			}
		}

		internal static void AddClient(IPEndPoint ipEndpoint)
        {
            if (instance.gameManager.clientList.Contains(ipEndpoint) == false)
            { // If it's a new client, add to the client list
                print($"Connect to {ipEndpoint}");
                instance.gameManager.clientList.Add(ipEndpoint);
            }
        }

        /// <summary>
        /// TODO: We need to add timestamps to timeout and remove clients from the list.
        /// </summary>
        internal static void RemoveClient(IPEndPoint ipEndpoint)
        {
            instance.gameManager.clientList.Remove(ipEndpoint);
        }
	}
}