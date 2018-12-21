using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
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
		public int player_amount = 2;
		public GameObject server_player_prefab;
		public GameObject ball_prefab;
		public GameObject server_fuel_prefab;
		public uint server_snapshot_rate = 0; //64hz;
		private uint server_tick_number;
		private uint server_tick_accumulator;
		private List<GameObject> server_players = new List<GameObject>();
		//private List<Rigidbody> server_rbs;
		//private Queue<GameOffMessage>[] game_off_msgs;
		private Queue<InputMessage> server_input_msgs = new Queue<InputMessage>();
		private Queue<GameManager.Client> client_queue = new Queue<GameManager.Client>();
		private int replayWatchers;

		public static ServerManager instance;
		private GameManager gameManager;

		private Vector3 placement = Vector3.zero;

		void Start ()
		{
			instance = this;
			gameManager = GameManager.instance;

			/*if (Globals.arena == "instance")
				placement = new Vector3(0,100,270);
			else if (Globals.arena == "hydrobase")
				*/placement.y = 100;

			replayWatchers = player_amount;

			GameManager.Client client = new GameManager.Client {
				id = 9001,
				name = "Ball",
				position = placement,
				rotation = Quaternion.identity
			};
			client_queue.Enqueue(client);
			gameManager.clientList.Add(client);

			for (var i=0; i<Globals.barrelCount; i++)
			{
				float angle = i * Mathf.PI / 3;
   				Vector3 pos = new Vector3(placement.x + Mathf.Cos(angle) * 100, 80, placement.z + (Mathf.Sin(angle) * 100)); //barrel placement
				client = new GameManager.Client {
					id = (uint)(9002 + i),
					name = "Barrel " + i,
					position = pos,
					rotation = Quaternion.identity
				};
				client_queue.Enqueue(client);
				gameManager.clientList.Add(client);
			}
		}
		
		void Update ()
		{
			while (client_queue.Count > 0)
			{
				GameManager.Client client = client_queue.Dequeue();
				GameObject newGuy;
				if (client.id == 9001) //ball
				{
					newGuy = Instantiate(instance.ball_prefab, client.position, client.rotation) as GameObject;
					newGuy.name = client.id.ToString();
					server_players.Add(newGuy);
				}
				else if (client.id > 9001 && client.id <= 9001 + Globals.barrelCount) //fuel
				{
					newGuy = Instantiate(instance.server_fuel_prefab, client.position, client.rotation) as GameObject;
					newGuy.name = client.id.ToString();
					server_players.Add(newGuy);
				}
				else
				{
					newGuy = Instantiate(instance.server_player_prefab, client.position, client.rotation) as GameObject;
					newGuy.name = client.id.ToString();
					server_players.Add(newGuy);
				}
			}

			if (Globals.inReplay)
			{
				if (replayWatchers == 0)
				{
					Debug.Log("Resuming game");
					Globals.inReplay = false;
					replayWatchers = player_amount;
					
					GameOnMessage unpauseGameMsg = new GameOnMessage();
					unpauseGameMsg.AckMsg = new AckMessage();
					BroadcastGameOnMessage(unpauseGameMsg);
					Debug.Log("Sent resume game msg");
				}
			}
			else if (Globals.gameOn)
			{
				if (server_players[0].GetComponent<Scorer>().somebodyScored)
				{
					Globals.inReplay = true;
					server_players[0].GetComponent<Scorer>().somebodyScored = false;
					server_input_msgs.Clear();

					server_players[0].transform.position = placement; //ball placement
					for (var i=1; i<=Globals.barrelCount; i++)
					{
						float angle = i * Mathf.PI / 3;
						Vector3 pos = new Vector3(placement.x + Mathf.Cos(angle) * 100, 80, placement.z + (Mathf.Sin(angle) * 100)); //barrel placement
						server_players[i].transform.position = pos;
					}
					for (var i=Globals.barrelCount+1; i<server_players.Count; i++)
					{
						float angle = i * Mathf.PI / 3;
						Vector3 pos = new Vector3(placement.x + Mathf.Cos(angle) * 100, 50, placement.z + (Mathf.Sin(angle) * 100)); //player placement
						server_players[i].transform.position = pos;
					}
					Debug.Log("Reset positions");

					//send score msg
					GameOnMessage gameOnMessage = new GameOnMessage();
					gameOnMessage.ScoreMsg = new ScoreMessage{
						Id = server_players[0].GetComponent<Scorer>().touchedLastId,
						Score = 0,
						Name = "",
						Team = 0
					};

					BroadcastGameOnMessage(gameOnMessage);
					Debug.Log("Sent scoremsg");
				}
				else
				{
					float fdt = Time.fixedDeltaTime;
					uint server_tick_number = this.server_tick_number;
					uint server_tick_accumulator = this.server_tick_accumulator;

					while (this.server_input_msgs.Count > 0) //was if instead of while so idk if it will work tbh
					{
						InputMessage input_msg = this.server_input_msgs.Dequeue();
						GameObject go = server_players.First(p => p.name == input_msg.Id.ToString());
						//Debug.Log(BoatAlignNormal.instance);
						//Debug.Log(go.GetComponent<Rigidbody>());
						if (input_msg.Inputs.Count > 0)
						{
							if (input_msg.Id < 9001)
							{
								go.GetComponent<JetSkiServerOptions>().rocketBoosting = input_msg.Inputs[0].RocketBoosting;
								go.GetComponent<JetSkiServerOptions>().waterBoosting = input_msg.Inputs[0].WaterBoosting;
							}

							BoatAlignNormal.instance.PrePhysicsStep(go.GetComponent<Rigidbody>(), input_msg.Inputs[0], fdt, Time.deltaTime);
						}
						else
						{
							BoatAlignNormal.instance.PrePhysicsStep(go.GetComponent<Rigidbody>(), new Inputs{}, fdt, Time.deltaTime);
						}

						if(Vector3.Distance(placement, go.transform.position) > 750) //keep within dome
						{
							go.GetComponent<Rigidbody>().AddForce((placement - go.transform.position).normalized * 200 * fdt, ForceMode.VelocityChange);
						}
					}

					BoatAlignNormal.instance.PrePhysicsStep(server_players[0].GetComponent<Rigidbody>(), new Inputs{}, fdt, Time.deltaTime);
					if(Vector3.Distance(placement, server_players[0].transform.position) > 750) //keep within dome
					{
						server_players[0].GetComponent<Rigidbody>().AddForce((placement - server_players[0].transform.position).normalized * 200 * fdt, ForceMode.VelocityChange);
					}

					for (var i=0; i<Globals.barrelCount; i++)
					{
						BoatAlignNormal.instance.PrePhysicsStep(server_players[i+1].GetComponent<Rigidbody>(), new Inputs{}, fdt, Time.deltaTime);
						if(Vector3.Distance(placement, server_players[i+1].transform.position) > 750) //keep within dome
						{
							server_players[i+1].GetComponent<Rigidbody>().AddForce((placement - server_players[i+1].transform.position).normalized * 200 * fdt, ForceMode.VelocityChange);
						}
					}

					Physics.Simulate(fdt);

					++server_tick_number;

					for (var i=0; i<gameManager.clientList.Count;i++)
					{
						GameObject pkmngo = server_players.First(p => p.name == gameManager.clientList[i].id.ToString());
						Rigidbody rb = pkmngo.GetComponent<Rigidbody>();

						GameOnMessage gameOnMessage = new GameOnMessage();
						/*if (gameManager.clientList[i].id < 9001)
						{
							if (System.Convert.ToUInt16(pkmngo.name) < 9001) Debug.Log("Player ID: " + gameManager.clientList[i].id + " WaterBoosting: " + pkmngo.GetComponent<JetSkiServerOptions>().waterBoosting.ToString() + " RocketBoosting: " + pkmngo.GetComponent<JetSkiServerOptions>().rocketBoosting.ToString());
							*/gameOnMessage.ServerStateMsg = new StateMessage{
								Id = gameManager.clientList[i].id,
								DeliveryTime = Time.time + gameManager.latency,// + 0.1f;//+ this._pingTime[this._pingTime.Count-1];
								TickNumber = server_tick_number, //maybe currently broken
								Position = rb.transform.position,
								Rotation = rb.transform.rotation,
								Velocity = rb.velocity,
								AngularVelocity = rb.angularVelocity,
								WaterBoosting = (gameManager.clientList[i].id < 9001) ? pkmngo.GetComponent<JetSkiServerOptions>().waterBoosting : true,
								RocketBoosting = (gameManager.clientList[i].id < 9001) ? pkmngo.GetComponent<JetSkiServerOptions>().rocketBoosting : true
							};
						/*}
						else
						{
							gameOnMessage.ServerStateMsg = new StateMessage{
								Id = gameManager.clientList[i].id,
								DeliveryTime = Time.time + gameManager.latency,// + 0.1f;//+ this._pingTime[this._pingTime.Count-1];
								TickNumber = server_tick_number,
								Position = rb.transform.position,
								Rotation = rb.transform.rotation,
								Velocity = rb.velocity,
								AngularVelocity = rb.angularVelocity
							};
						}*/

						BroadcastGameOnMessage(gameOnMessage);
					}

					this.server_tick_number = server_tick_number;
					this.server_tick_accumulator = server_tick_accumulator;
				}
			}
			else if (gameManager.clientList.Count == 1 + Globals.barrelCount + player_amount)
			{
				Globals.gameOn = true;
				GameOffMessage gameOffMessage = new GameOffMessage();
				gameOffMessage.StartGameMsg = new StartGameMessage();
				BroadcastGameOffMessage(gameOffMessage);
			}
		}

		internal static void HandleData(byte[] data, IPEndPoint iPEndPoint)
		{
			if (Globals.gameOn)
			{
				GameOnMessage msg = GameOnMessage.Parser.ParseFrom(data);
				//Debug.Log("GameOnMessage: " + msg.ToString());
				switch ((int)msg.GameOnCase)
				{
					case 1: //*****GET INPUT MESSAGE (UNTESTED)*****
						//Debug.Log("Input from client " + msg.ClientInputMsg.Id);
						instance.server_input_msgs.Enqueue(msg.ClientInputMsg);
						//instance.server_input_msgs[instance.gameManager.clientList.FindIndex(c => c.id == msg.ClientInputMsg.Id)].Enqueue(msg.ClientInputMsg);
					break;

					case 6: //*****CLIENT ACK RECEIVED (UNTESTED)***** (right now just using this for when clients are done watching replay)
						instance.replayWatchers--;
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
						//Debug.Log("Request to join from client " + msg.JoinServerMsg.Name);
                        AddClient(iPEndPoint, msg.JoinServerMsg.Name);
					break;
					case 5: //*****ACK FROM CLIENT (UNIMPLEMENTED)*****
						//Debug.Log("Ack from client " + msg.AckMsg.Id);
					break;
				}
			}
		}

		internal static void AddClient(IPEndPoint ipEndpoint, string client_name)
        {
			//Debug.Log("Attempting to add client");
            //bool clientExists = instance.gameManager.clientList.Any(c => c.ipEndPoint.Equals(ipEndpoint));

            //if (!clientExists)
            //{
				//var i = instance.gameManager.clientList.Count; //id for new client
				//float angle = i * Mathf.PI * 2 / instance.gameManager.clientList.Count;
   				//Vector3 pos = new Vector3(0, 50, 270);// = new Vector3(Mathf.Cos(angle), 50, Mathf.Sin(angle)); //placement of new client

                GameManager.Client client = new GameManager.Client {
                    id = (uint)instance.gameManager.clientList.Count,
                    ipEndPoint = ipEndpoint,
                    name = client_name,
					position = instance.placement,
					rotation = Quaternion.identity
                };

				float angle = client.id * Mathf.PI / 3;
   				Vector3 pos = new Vector3(instance.placement.x + Mathf.Cos(angle) * 100, 80, instance.placement.z + (Mathf.Sin(angle) * 100));
				client.position = pos;

				//spawn clients under ball for now
				if (client.id != 9001)
					client.position.y -= 50;

				Google.Protobuf.Collections.RepeatedField<NewPlayerMessage> newPlayerMessages = new Google.Protobuf.Collections.RepeatedField<NewPlayerMessage>();

				if (instance.gameManager.clientList.Count > 0) //more players already connected (message includes ball)
				{
					//Debug.Log("more players already connected");
					GameOffMessage gameoffmsg = new GameOffMessage();
					gameoffmsg.NewPlayerMsg = new NewPlayerMessage {
						Id = client.id,
						Name = client.name,
						Position = client.position,
						Rotation = client.rotation
					};

					for (int j = 0; j<instance.gameManager.clientList.Count; j++)
					{
						newPlayerMessages.Add(
							new NewPlayerMessage {
								Id = instance.gameManager.clientList[j].id,
								Name = instance.gameManager.clientList[j].name,
								Team = instance.gameManager.clientList[j].team,
								Position = instance.gameManager.clientList[j].position,
								Rotation = instance.gameManager.clientList[j].rotation
							}
						);

						//tell existing player about new player
						if (j > Globals.barrelCount) //don't send it to the ball or fuel packs
						instance.gameManager.connection.Send(gameoffmsg.ToByteArray(), instance.gameManager.clientList[j].ipEndPoint);
					}
				}
				else //only tell new client about the ball and fuel packs
				{
					//Debug.Log("only the ball: " + instance.gameManager.clientList[0].id);
					newPlayerMessages.Add(
						new NewPlayerMessage {
							Id = instance.gameManager.clientList[0].id,
							Name = instance.gameManager.clientList[0].name,
							Position = instance.gameManager.clientList[0].position,
							Rotation = instance.gameManager.clientList[0].rotation
						}
					);

					for (var k=1; k<=Globals.barrelCount; k++)
					{
						newPlayerMessages.Add(
							new NewPlayerMessage {
								Id = instance.gameManager.clientList[k].id,
								Name = instance.gameManager.clientList[k].name,
								Position = instance.gameManager.clientList[k].position,
								Rotation = instance.gameManager.clientList[k].rotation
							}
						);
					}
				}

                //Debug.Log("Adding client " + ipEndpoint.Address.ToString());
                instance.gameManager.clientList.Add(client);
				instance.client_queue.Enqueue(client);
				//instance.OffMainThreadInstantiate(client);
				//instance.server_rbs.Add(newGuy.GetComponent<Rigidbody>());

                //tell new client their id and team, which map to load, and where to spawn
				GameOffMessage gameOffMessage = new GameOffMessage();
				gameOffMessage.AcceptJoinMsg = new AcceptJoinMessage {
					Id = client.id, 
					Arena = Globals.arena, 
					Team = 1, 
					Position = client.position, 
					Rotation = client.rotation
				};
				gameOffMessage.AcceptJoinMsg.NewPlayerMsg.AddRange(newPlayerMessages);
				instance.gameManager.connection.Send(gameOffMessage.ToByteArray(), client.ipEndPoint);
			//}
        }

        /// <summary>
        /// TODO: We need to add timestamps to timeout and remove clients from the list.
        /// </summary>
        internal static void RemoveClient(IPEndPoint ipEndpoint)
        {
            instance.gameManager.clientList.RemoveAll(c => c.ipEndPoint.Equals(ipEndpoint));
        }

		internal static void BroadcastGameOffMessage(GameOffMessage gameOffMessage)
		{
			for (var i=1 + Globals.barrelCount; i<instance.gameManager.clientList.Count; i++)
			{
				instance.gameManager.connection.Send(gameOffMessage.ToByteArray(), instance.gameManager.clientList[i].ipEndPoint);
			}
		}

		internal static void BroadcastGameOnMessage(GameOnMessage gameOnMessage)
		{
			for (var i=1 + Globals.barrelCount; i<instance.gameManager.clientList.Count; i++)
			{
				instance.gameManager.connection.Send(gameOnMessage.ToByteArray(), instance.gameManager.clientList[i].ipEndPoint);
			}
		}

		IEnumerator InstantiatePlayer()
		{
			yield return new WaitForSeconds(0.5f);
			Instantiate(server_player_prefab);
		}
	}
}