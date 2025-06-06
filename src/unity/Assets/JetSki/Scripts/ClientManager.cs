﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.IO;
using Google.Protobuf;
using JetSkiProto;
using static JetSkiProto.InputMessage.Types;
using static JetSkiProto.ReplayMessage.Types;
using System;

namespace JetSki{

	public class ClientManager : MonoBehaviour {

		public struct ClientState
		{
			public Vector3 position;
			public Quaternion rotation;
		}

		public GameObject player_prefab;
		public GameObject ball_prefab;
		public GameObject fuel_prefab;

		private List<GameObject> players = new List<GameObject>();

		public bool client_enable_corrections = true;
		public bool client_correction_smoothing = false;
		public bool client_send_redundant_inputs = false;
		private float client_timer;
		private uint client_tick_number;
		private uint client_last_received_state_tick;
		private const int c_client_buffer_size = 32; //1024;
		private ClientState[] client_state_buffer; // client stores predicted moves here
		private Inputs[] client_input_buffer; // client stores predicted inputs here
		private Queue<StateMessage> client_state_msgs = new Queue<StateMessage>();
		private Vector3 client_pos_error;
		private Quaternion client_rot_error;
		private Queue<GameManager.Client> client_queue = new Queue<GameManager.Client>();

		public static ClientManager instance;
		private GameManager gameManager;

		private GameObject myGuy;

		private bool CameraLockedOnBall = false;

		ReplayMessage replay_msg = new ReplayMessage();
		StateMessage state_msg = new StateMessage();
		ReplayStateMessage replay_state_msg = new ReplayStateMessage();
		private uint replay_start_tick = 0;
		private uint replay_curr_tick = 0;
		private int replay_curr_index = 0;
		IOrderedEnumerable<ReplayStateMessage> sortedReplayStateMsgs;
		private bool sent_ack = false;

		void Start () {
			instance = this;
			gameManager = GameManager.instance;

            this.client_timer = 0.0f;
            this.client_tick_number = 0;
            this.client_last_received_state_tick = 0;
            this.client_state_buffer = new ClientState[c_client_buffer_size];
            this.client_input_buffer = new Inputs[c_client_buffer_size];
            this.client_pos_error = Vector3.zero;
            this.client_rot_error = Quaternion.identity;
			replay_msg.Id = 0;
			replay_msg.Name = "TestReplay";

			GameOffMessage gameOffMessage = new GameOffMessage();
			gameOffMessage.JoinServerMsg = new JoinServerMessage{Name = "Player"};
			gameManager.connection.Send(gameOffMessage.ToByteArray(), gameManager.clientList[0].ipEndPoint);
		}
		
		void Update () 
		{
			while (client_queue.Count > 0)
			{
				GameManager.Client client = client_queue.Dequeue();
				GameObject newGuy;

				if (client.id == 9001) //BALL
				{
					newGuy = Instantiate(ball_prefab, client.position, client.rotation) as GameObject;
					newGuy.name = client.id.ToString();
					players.Add(newGuy);
				}
				else if (client.id > 9001 && client.id <= 9001 + Globals.barrelCount) //FUEL PACK
				{
					newGuy = Instantiate(fuel_prefab, client.position, client.rotation) as GameObject;
					newGuy.name = client.id.ToString();
					players.Add(newGuy);
				}
				else //PLAYER
				{
					newGuy = Instantiate(player_prefab, client.position, client.rotation) as GameObject;
					newGuy.name = client.id.ToString();
					//JetSkiOptions jso = newGuy.GetComponent<JetSkiOptions>();
					//jso.enabled = true;
					//jso.InitializeStuff();
					//newGuy.transform.Find("JetFire").gameObject.GetComponent<ParticleSystem>().Play()
					players.Add(newGuy);
				}

				if (newGuy.name.Equals(Globals.myId.ToString()))
				{
					myGuy = newGuy;
				}
			}

			if (Globals.inReplay)
			{
				if(replay_curr_tick < client_tick_number)
				{
					if (replay_curr_index < sortedReplayStateMsgs.Count()-1)
					{
						Camera.main.transform.LookAt(players[0].transform.position, Vector3.up);
						while (sortedReplayStateMsgs.ElementAt(replay_curr_index).TickNumber == replay_curr_tick)
						{
							//Debug.Log("In replay with index " + replay_curr_index + " and tick " + replay_curr_tick);
							ReplayStateMessage rsm = sortedReplayStateMsgs.ElementAt(replay_curr_index);
							GameObject go = players.First(p => p.name == rsm.Id.ToString());
							if(go != null) 
							{
								go.transform.SetPositionAndRotation(rsm.Position, rsm.Rotation);
								if (rsm.Id < 9001) //it's a player
								{
									go.GetComponent<JetSkiOptions>().rocketBoosting = rsm.RocketBoosting;
									go.GetComponent<JetSkiOptions>().waterBoosting = rsm.WaterBoosting;
								}
							}
							replay_curr_index++;
						}
					}
					
					replay_curr_tick++;
				}
				else if (!sent_ack)
				{
					sent_ack = true;
					Debug.Log("Done watching replay");
					GameOnMessage gameOnMessage = new GameOnMessage();
					gameOnMessage.AckMsg = new AckMessage();
					gameManager.connection.Send(gameOnMessage.ToByteArray(), gameManager.clientList[0].ipEndPoint);
				}
			} 
			else if (Globals.gameOn)
			{
				if (myGuy != null)
				{
					Camera.main.transform.position = myGuy.transform.GetChild(1).position;
					Camera.main.transform.LookAt(myGuy.transform.position, Vector3.up);
				}

				float fdt = Time.fixedDeltaTime;

				float client_timer = this.client_timer;
				//uint client_tick_number = this.client_tick_number;
				client_timer += Time.deltaTime;

				CameraLockedOnBall = Input.GetKeyDown(KeyCode.Space) | Input.GetButtonDown("Jump");

				Inputs inputs = new Inputs{
					Forward = 0 + Input.GetAxis("Vertical"),
					Sideways = 0 + Input.GetAxis("Horizontal"),
					WaterBoosting = Input.GetMouseButton(1) | Input.GetButton("Fire1"),
					RocketBoosting = Input.GetMouseButton(0) | Input.GetButton("Fire2")
				};

				//***SEND THE STUFF (UNSTABLE)***
				GameOnMessage gameOnMessage = new GameOnMessage();
				gameOnMessage.ClientInputMsg = new InputMessage{
					Id = Globals.myId,
					DeliveryTime = Time.time + gameManager.latency, //BROKEN LATENCY VAL
					StartTickNumber = this.client_send_redundant_inputs ? this.client_last_received_state_tick : client_tick_number,
					Inputs = {inputs}
				};

				gameManager.connection.Send(gameOnMessage.ToByteArray(), gameManager.clientList[0].ipEndPoint);

				client_tick_number++;

				while (this.ClientHasStateMessage())
				{
					state_msg = client_state_msgs.Dequeue();
					
					replay_state_msg = new ReplayStateMessage{
						Id = state_msg.Id,
						DeliveryTime = state_msg.DeliveryTime,
						TickNumber = client_tick_number,//state_msg.TickNumber;
						Position = state_msg.Position,
						Rotation = state_msg.Rotation,
						RocketBoosting = state_msg.RocketBoosting,
						WaterBoosting = state_msg.WaterBoosting
					};

					replay_msg.ReplayStateMsgs.Add(replay_state_msg);

					GameObject go = players.First(p => p.name == state_msg.Id.ToString());
					if(go != null)
					{
						go.transform.SetPositionAndRotation(state_msg.Position, state_msg.Rotation);
						if (state_msg.Id < 9001) //it's a player
						{
							go.GetComponent<JetSkiOptions>().rocketBoosting = state_msg.RocketBoosting;
							go.GetComponent<JetSkiOptions>().waterBoosting = state_msg.WaterBoosting;
						}
					}
				}

				this.client_timer = client_timer;
				//this.client_tick_number = client_tick_number;
			}
		}

		private bool ClientHasStateMessage()
        {
            return client_state_msgs.Count > 0;// && Time.time >= this.client_state_msgs.Peek().DeliveryTime; //JUST COUNT FOR NOW, NOT TIME-BASED
        }

		private bool ClientHasReplayStateMessage()
		{
			return replay_msg.ReplayStateMsgs.Count > 0;
		}

		internal static void HandleData(byte[] data, IPEndPoint iPEndPoint)
		{
			if (Globals.gameOn)
			{
				GameOnMessage msg = GameOnMessage.Parser.ParseFrom(data);
				switch ((int)msg.GameOnCase)
				{
					case 2: //*****GET STATE MESSAGE (UNSTABLE)*****
						instance.client_state_msgs.Enqueue(msg.ServerStateMsg);
					break;

					case 3: //*****SCORE UPDATE (UNTESTED)*****
						//UpdateScore(msg.ScoreMsg.Id, msg.ScoreMsg.Score, msg.ScoreMsg.Name, msg.ScoreMsg.Team);

						instance.replay_start_tick = instance.client_tick_number - 300;
						if (instance.replay_start_tick < 0) instance.replay_start_tick = 0;
						instance.replay_curr_tick = instance.replay_start_tick;

						instance.sortedReplayStateMsgs = instance.replay_msg.ReplayStateMsgs.OrderBy(r => r.TickNumber);
						//Debug.Log("Sorted replay messages");

						instance.replay_curr_index = FindTickNumIndex(instance.sortedReplayStateMsgs, instance.replay_start_tick);
						Debug.Log("Current replay index: " + instance.replay_curr_index);
						Debug.Log("Total replay messages: " + instance.sortedReplayStateMsgs.Count());
						Debug.Log("Replay start tick: " + instance.replay_start_tick);
						Debug.Log("Current tick: " + instance.client_tick_number);

						instance.sent_ack = false;

						Camera.main.transform.position = new Vector3(0, 50, 0);

						Globals.inReplay = true;
					break;

					case 4: //*****STOP GAME (UNTESTED)*****
						Globals.gameOn = false;
					break;

					case 6: //*****RESUME GAME AFTER REPLAY (UNTESTED)*****
						Globals.inReplay = false; //using ack message because I did not think this through hard enough while setting up the message flow
					break;
				}
			}
			else
			{
				GameOffMessage msg = GameOffMessage.Parser.ParseFrom(data);
				switch ((int)msg.GameOffCase)
				{
					case 2: //*****ACCEPTED TO JOIN SERVER (UNSTABLE)*****
						Globals.myId = msg.AcceptJoinMsg.Id;
						Globals.arena = msg.AcceptJoinMsg.Arena;

						Debug.Log("Joining server in arena " + Globals.arena + " with " + msg.AcceptJoinMsg.NewPlayerMsg.Count + " other.");

						SceneManager.LoadScene(Globals.arena);
						
						instance.JoinServer(msg.AcceptJoinMsg.Id, msg.AcceptJoinMsg.Team, msg.AcceptJoinMsg.Position, msg.AcceptJoinMsg.Rotation, msg.AcceptJoinMsg.NewPlayerMsg);
					break;

					case 3: //*****NEW OTHER PLAYERS (UNSTABLE)*****
						instance.NewOtherPlayer(msg.NewPlayerMsg.Id, msg.NewPlayerMsg.Name, msg.NewPlayerMsg.Team, msg.NewPlayerMsg.Position, msg.NewPlayerMsg.Rotation);
					break;

					case 4: //*****START GAME (UNSTABLE)*****
						Globals.gameOn = true;
					break;
				}
			}
		}

        private static void UpdateScore(uint id, uint score, string name, uint team)
        {
            throw new NotImplementedException();
        }

        //CLIENT MESSAGE HANDLERS GAMEOFF

        private void JoinServer(uint id, uint team, Vector3 position, Quaternion rotation, Google.Protobuf.Collections.RepeatedField<NewPlayerMessage> newPlayerMessages)
        {
			GameManager.Client client = new GameManager.Client {
				id = id,
				name = Globals.myName,
				team = team,
				position = position,
				rotation = rotation
			};
			client_queue.Enqueue(client);
			gameManager.clientList.Add(client); //SELF

			if (newPlayerMessages.Count > 0)
			{
				for (var i = 0; i<newPlayerMessages.Count; i++)
				{
					NewOtherPlayer(newPlayerMessages[i].Id, newPlayerMessages[i].Name, newPlayerMessages[i].Team, newPlayerMessages[i].Position, newPlayerMessages[i].Rotation);
				}
			}
        }

        private void NewOtherPlayer(uint id, string name, uint team, Vector3 position, Quaternion rotation)
        {
			GameManager.Client client = new GameManager.Client{
				id = id,
				name = name,
				team = team,
				position = position,
				rotation = rotation
			};
			client_queue.Enqueue(client);
			gameManager.clientList.Add(client); //OTHER PLAYER OR BALL
        }

		public static int FindTickNumIndex(IOrderedEnumerable<ReplayStateMessage> items, uint ticknum) {
            int index = 0;
            foreach (var item in items) {
				//Debug.Log("Checking item " + index + " with ticknum " + item.TickNumber + " and ID " + item.Id);
                if (item.TickNumber == ticknum) break;
                index++;
            }
            return index;
        }

        /*private void ClientStoreCurrentStateAndStep(ref ClientState current_state, Rigidbody rigidbody, Inputs inputs, float fdt, float dt)
        {
            current_state.position = rigidbody.transform.position;
            current_state.rotation = rigidbody.transform.rotation;

            this.PrePhysicsStep(rigidbody, inputs, fdt, dt);
            //Physics.SyncTransforms();
            Physics.Simulate(fdt);
        }*/
	}
}
