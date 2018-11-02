using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.IO;
using Google.Protobuf;
using JetSkiProto;
using static JetSkiProto.InputMessage.Types;

namespace JetSki{

	public class ClientManager : MonoBehaviour {

		public struct ClientState
		{
			public Vector3 position;
			public Quaternion rotation;
		}

		// client specific
		public GameObject player_prefab;
		public GameObject ball_prefab;

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
		//private Queue<StateMessage>[] other_player_state_msgs;
		private Vector3 client_pos_error;
		private Quaternion client_rot_error;
		private Queue<GameManager.Client> client_queue = new Queue<GameManager.Client>();

		public static ClientManager instance;
		private GameManager gameManager;

		private GameObject myGuy;

		void Start () {
			instance = this;
			gameManager = GameManager.instance;
			//this.client_proxy_rigidbody = this.client_proxy.GetComponent<Rigidbody>();
            this.client_timer = 0.0f;
            this.client_tick_number = 0;
            this.client_last_received_state_tick = 0;
            this.client_state_buffer = new ClientState[c_client_buffer_size];
            this.client_input_buffer = new Inputs[c_client_buffer_size];
            //this.client_state_msgs = new Queue<StateMessage>();
            this.client_pos_error = Vector3.zero;
            this.client_rot_error = Quaternion.identity;
			//Debug.Log("Attempting to join " + gameManager.clientList[0].ipEndPoint.Address);
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
				if (client.id == 9001)
				{
					newGuy = Instantiate(ball_prefab, client.position, client.rotation);
					newGuy.name = client.id.ToString();
					players.Add(newGuy);
				}
				else
				{
					newGuy = Instantiate(player_prefab, client.position, client.rotation);
					newGuy.name = client.id.ToString();
					players.Add(newGuy);
				}
				if (newGuy.name.Equals(Globals.myId.ToString()))
				{
					//Debug.Log("My guy");
					//Debug.Log(LerpCam.instance);
					//LerpCam lerp = Camera.main.gameObject.GetComponent<LerpCam>();
					//Debug.Log(lerp);
					myGuy = newGuy;
					//lerp.enabled = true;
					//lerp._targetPos = newGuy.transform.GetChild(1);
					//lerp._targetLookatPos = newGuy.transform;
					//Debug.Log(LerpCam.instance._targetPos);
					//Debug.Log(LerpCam.instance._targetLookatPos);
				}
			}
			if (Globals.gameOn)
			{
				if (myGuy)
				{
					Camera.main.transform.position = myGuy.transform.GetChild(1).position;
					Camera.main.transform.LookAt(myGuy.transform.position, Vector3.up);
				}
				

				float fdt = Time.fixedDeltaTime;

				float client_timer = this.client_timer;
				uint client_tick_number = this.client_tick_number;
				client_timer += Time.deltaTime;

				//CameraLockedOnBall = Input.GetKeyDown(KeyCode.Space) | Input.GetButtonDown("Jump");

				Inputs inputs = new Inputs{
					Forward = 0 + Input.GetAxis("Vertical"),
					Sideways = 0 + Input.GetAxis("Horizontal"),
					RocketBoosting = Input.GetMouseButton(0) | Input.GetButton("Fire2"),
					WaterBoosting = Input.GetMouseButton(1) | Input.GetButton("Fire1")
				};

				//***SEND THE STUFF (UNTESTED)***
				//Debug.Log("Sending input_msg");
				GameOnMessage gameOnMessage = new GameOnMessage();
				gameOnMessage.ClientInputMsg = new InputMessage{
					Id = Globals.myId,
					DeliveryTime = Time.time + gameManager.latency,// + 0.1f;//this._pingTime[this._pingTime.Count - 1],
					StartTickNumber = this.client_send_redundant_inputs ? this.client_last_received_state_tick : client_tick_number,
					Inputs = {inputs}
				};

				gameManager.connection.Send(gameOnMessage.ToByteArray(), gameManager.clientList[0].ipEndPoint);

				++client_tick_number;

				while (this.ClientHasStateMessage()) //change to while from if
				{
					StateMessage state_msg = this.client_state_msgs.Dequeue();
					Debug.Log("Checking state: " + state_msg.Id);
					GameObject go = players.First(p => p.name == state_msg.Id.ToString());
					//if(go.transform)
					go.transform.SetPositionAndRotation(state_msg.Position,state_msg.Rotation);


				}

				this.client_timer = client_timer;
				this.client_tick_number = client_tick_number;

			}
		}

		private bool ClientHasStateMessage()
        {
            return this.client_state_msgs.Count > 0;// && Time.time >= this.client_state_msgs.Peek().DeliveryTime;
        }

		internal static void HandleData(byte[] data, IPEndPoint iPEndPoint)
		{
			//if ()
			if (Globals.gameOn)
			{
				GameOnMessage msg = GameOnMessage.Parser.ParseFrom(data);
				//Debug.Log("GameOnMessage: " + msg.ToString());
				switch ((int)msg.GameOnCase)
				{
					case 2: //*****GET STATE MESSAGE (UNTESTED)*****
						//Debug.Log("ID: " + msg.ServerStateMsg.Id);
						instance.client_state_msgs.Enqueue(msg.ServerStateMsg);
					break;

					case 3: //*****SCORE UDPATE (UNIMPLEMENTED)*****
						//Debug.Log("Somebody scored.");
						//Debug.Log("ID: " + msg.ScoreMsg.Id);
						//Debug.Log("Score: " + msg.ScoreMsg.Score);
					break;

					case 4: //*****STOP GAME (UNTESTED)*****
						Globals.gameOn = false;
					break;
				}
			}
			else
			{
				GameOffMessage msg = GameOffMessage.Parser.ParseFrom(data);
				//Debug.Log("GameOffMessage: " + msg.ToString());
				switch ((int)msg.GameOffCase)
				{
					case 2: //*****ACCEPTED TO JOIN SERVER (UNTESTED)*****
						Debug.Log("Joining server with " + msg.AcceptJoinMsg.NewPlayerMsg.Count + " other.");
						//Debug.Log("Team: " + msg.AcceptJoinMsg.Team);
						//Debug.Log("Position: " + msg.AcceptJoinMsg.Position);
						//Debug.Log("Rotation: " + msg.AcceptJoinMsg.Rotation);

						Globals.myId = msg.AcceptJoinMsg.Id;

						//if (msg.AcceptJoinMsg.NewPlayerMsg.Count > 0)
						instance.JoinServer(msg.AcceptJoinMsg.Id, msg.AcceptJoinMsg.Team, msg.AcceptJoinMsg.Position, msg.AcceptJoinMsg.Rotation, msg.AcceptJoinMsg.NewPlayerMsg);
						//else
						//instance.JoinServer(msg.AcceptJoinMsg.Id, msg.AcceptJoinMsg.Team, msg.AcceptJoinMsg.Position, msg.AcceptJoinMsg.Rotation);
					break;

					case 3: //*****NEW OTHER PLAYERS (UNTESTED)*****
						Debug.Log("New player joined.");
						//Debug.Log("ID: " + msg.NewPlayerMsg.Id);
						//Debug.Log("Team: " + msg.NewPlayerMsg.Team);
						//Debug.Log("Position: " + msg.NewPlayerMsg.Position);
						//Debug.Log("Rotation: " + msg.NewPlayerMsg.Rotation);
						instance.NewOtherPlayer(msg.NewPlayerMsg.Id, msg.NewPlayerMsg.Name, msg.NewPlayerMsg.Team, msg.NewPlayerMsg.Position, msg.NewPlayerMsg.Rotation);
					break;

					case 4: //*****START GAME (UNTESTED)*****
						Globals.gameOn = true;
					break;
				}
			}
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
					/*client = new GameManager.Client {
						id = newPlayerMessages[i].Id,
						name = newPlayerMessages[i].Name,
						team = newPlayerMessages[i].Team,
						position = newPlayerMessages[i].Position,
						rotation = newPlayerMessages[i].Rotation
					};
					client_queue.Enqueue(client);
					gameManager.clientList.Add(client); //OTHER PLAYER OR BALL*/
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
			gameManager.clientList.Add(client);
			
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
