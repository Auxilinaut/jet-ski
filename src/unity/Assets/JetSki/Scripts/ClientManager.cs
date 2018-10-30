using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;                   //Allows us to use UI.
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
		private UdpConnectedClient connection;
		public GameObject client_player;
		public GameObject client_proxy;
		private Rigidbody client_proxy_rigidbody;

		public GameObject other_player_prefab;
		public GameObject[] other_player;
		private Rigidbody[] other_player_rigidbody;

		public bool client_enable_corrections = true;
		public bool client_correction_smoothing = false;
		public bool client_send_redundant_inputs = false;
		private float client_timer;
		private uint client_tick_number;
		private uint client_last_received_state_tick;
		private const int c_client_buffer_size = 32; //1024;
		private ClientState[] client_state_buffer; // client stores predicted moves here
		private Inputs[] client_input_buffer; // client stores predicted inputs here
		private Queue<StateMessage> client_state_msgs;
		private Queue<StateMessage>[] other_player_state_msgs;
		private Vector3 client_pos_error;
		private Quaternion client_rot_error;

		public static ClientManager instance;

		public void InitGame(UdpConnectedClient connection)
		{
			this.connection = connection;
		}

		// Use this for initialization
		void Start () {
			instance = this;
			this.client_proxy_rigidbody = this.client_proxy.GetComponent<Rigidbody>();
            this.client_timer = 0.0f;
            this.client_tick_number = 0;
            this.client_last_received_state_tick = 0;
            this.client_state_buffer = new ClientState[c_client_buffer_size];
            this.client_input_buffer = new Inputs[c_client_buffer_size];
            this.client_state_msgs = new Queue<StateMessage>();
            this.client_pos_error = Vector3.zero;
            this.client_rot_error = Quaternion.identity;
		}
		
		// Update is called once per frame
		void Update () {
			if (Globals.gameOn)
			{
				float fdt = Time.fixedDeltaTime;

				float client_timer = this.client_timer;
				uint client_tick_number = this.client_tick_number;
				client_timer += Time.deltaTime;

				CameraLockedOnBall = Input.GetKeyDown(KeyCode.Space) | Input.GetButtonDown("Jump");

				Inputs inputs = new Inputs{
					Forward = 0 + Input.GetAxis("Vertical"),
					Sideways = 0 + Input.GetAxis("Horizontal"),
					RocketBoosting = Input.GetMouseButton(0) | Input.GetButton("Fire2"),
					WaterBoosting = Input.GetMouseButton(1) | Input.GetButton("Fire1")
				};

				//***SEND THE STUFF (UNTESTED)***
				Debug.Log("Sending input_msg");
				InputMessage input_msg = new InputMessage{
					DeliveryTime = Time.time + this.latency,// + 0.1f;//this._pingTime[this._pingTime.Count - 1],
					StartTickNumber = this.client_send_redundant_inputs ? this.client_last_received_state_tick : client_tick_number,
					Inputs = {inputs}
				};

				connection.Send(input_msg.ToByteArray(), clientList[0]);

				++client_tick_number;

				/*while (client_timer >= fdt)
				{
					client_timer -= fdt;

					uint buffer_slot = client_tick_number % c_client_buffer_size;

					Inputs inputs = new Inputs{
						Forward = 0 + Input.GetAxis("Vertical"),
						Sideways = 0 + Input.GetAxis("Horizontal"),
						RocketBoosting = Input.GetMouseButton(0) | Input.GetButton("Fire2"),
						WaterBoosting = Input.GetMouseButton(1) | Input.GetButton("Fire1")
					};
					
					this.client_input_buffer[buffer_slot] = inputs;

					// store state for this tick, then use current state + input to step simulation
					this.ClientStoreCurrentStateAndStep(
						ref this.client_state_buffer[buffer_slot],
						client_proxy_rigidbody,
						inputs,
						fdt, client_timer);

					//***SEND THE STUFF (UNTESTED)***
					Debug.Log("Sending input_msg");
					InputMessage input_msg = new InputMessage{
						DeliveryTime = Time.time + this.latency,// + 0.1f;//this._pingTime[this._pingTime.Count - 1],
						StartTickNumber = this.client_send_redundant_inputs ? this.client_last_received_state_tick : client_tick_number,
						Inputs = {new Inputs ()}
					};

					input_msg.Inputs.Add(inputs);

					for (uint tick = input_msg.StartTickNumber; tick <= client_tick_number; ++tick)
					{
						input_msg.Inputs.Add(this.client_input_buffer[tick % c_client_buffer_size]);
					}

					connection.Send(input_msg.ToByteArray(), clientList[0]);

					++client_tick_number;
				}*/

				//***RECEIVE THE STUFF (UNTESTED)***
				/*if (theData != null)
				{
					Debug.Log("Received state message");
					this.client_state_msgs.Enqueue(StateMessage.Parser.ParseFrom(theData));
					theData = null;
				}*/

				if (this.ClientHasStateMessage())
				{
					StateMessage state_msg = this.client_state_msgs.Dequeue();
					/*while (this.ClientHasStateMessage()) // make sure if there are any newer state messages available, we use those instead
					{
						state_msg = this.client_state_msgs.Dequeue();
					}*/

					this.client_last_received_state_tick = state_msg.TickNumber;

					client_proxy_rigidbody.transform.SetPositionAndRotation(state_msg.Position, state_msg.Rotation);
					//client_proxy_rigidbody.transform.position = state_msg.Position;
					//client_proxy_rigidbody.transform.rotation = state_msg.Rotation;



					/*if (this.client_enable_corrections)
					{
						uint buffer_slot = state_msg.TickNumber % c_client_buffer_size;
						Vector3 position_error = state_msg.Position - this.client_state_buffer[buffer_slot].position;
						float rotation_error = 1.0f - Quaternion.Dot(state_msg.Rotation, this.client_state_buffer[buffer_slot].rotation);

						if (position_error.sqrMagnitude > 0.0000001f || rotation_error > 0.00001f)
						{
							// capture the current predicted pos for smoothing
							Vector3 prev_pos = client_proxy_rigidbody.transform.position + this.client_pos_error;
							Quaternion prev_rot = client_proxy_rigidbody.transform.rotation * this.client_rot_error;

							// rewind & replay
							client_proxy_rigidbody.transform.position = state_msg.Position;
							client_proxy_rigidbody.transform.rotation = state_msg.Rotation;
							client_proxy_rigidbody.velocity = state_msg.Velocity;
							client_proxy_rigidbody.angularVelocity = state_msg.AngularVelocity;

							uint rewind_tick_number = state_msg.TickNumber;
							//StartCoroutine(DoTheShit(rewind_tick_number,dt));
							while (rewind_tick_number < client_tick_number)
							{
								buffer_slot = rewind_tick_number % c_client_buffer_size;
								this.ClientStoreCurrentStateAndStep(
									ref this.client_state_buffer[buffer_slot],
									client_proxy_rigidbody,
									this.client_input_buffer[buffer_slot],
									fdt, client_timer);

								++rewind_tick_number;
							}

							// if more than 2ms apart, just snap
							if ((prev_pos - client_proxy_rigidbody.transform.position).sqrMagnitude > 4.0f)//.sqrMagnitude >= 4.0f)
							{
								this.client_pos_error = Vector3.zero;
								this.client_rot_error = Quaternion.identity;
							}
							else
							{
								this.client_pos_error = prev_pos - client_proxy_rigidbody.transform.position;
								this.client_rot_error = Quaternion.Inverse(client_proxy_rigidbody.transform.rotation) * prev_rot;
							}
						}
					}*/

				}
				else
				{
					Debug.Log("No state message.");
				}

				this.client_timer = client_timer;
				this.client_tick_number = client_tick_number;

				/*if (this.client_correction_smoothing)
				{
					this.client_pos_error *= 0.9f;
					this.client_rot_error = Quaternion.Slerp(this.client_rot_error, Quaternion.identity, 0.1f);
				}
				else
				{
					this.client_pos_error = Vector3.zero;
					this.client_rot_error = Quaternion.identity;
				}*/
				
				client_player.transform.SetPositionAndRotation(client_proxy_rigidbody.transform.position, client_proxy_rigidbody.transform.rotation);
				//client_player.transform.position = client_proxy_rigidbody.transform.position;// + this.client_pos_error;
				//client_player.transform.rotation = client_proxy_rigidbody.transform.rotation;// * this.client_rot_error;
			}
		}

		private bool ClientHasStateMessage()
        {
            return this.client_state_msgs.Count > 0;// && Time.time >= this.client_state_msgs.Peek().DeliveryTime;
        }

		internal static void HandleData(byte[] data)
		{
			if (Globals.gameOn)
			{
				GameOnMessage msg = GameOnMessage.Parser.ParseFrom(data);
				Debug.Log("GameOnMessage: " + msg.ToString());
				switch ((int)msg.GameOnCase)
				{
					case 2: //*****GET STATE MESSAGE (UNIMPLEMENTED)*****
						Debug.Log("ID: " + msg.ServerStateMsg.Id);
					break;

					case 3: //*****SCORE UDPATE (UNIMPLEMENTED)*****
						Debug.Log("Somebody scored.");
						Debug.Log("ID: " + msg.ScoreMsg.Id);
						Debug.Log("Score: " + msg.ScoreMsg.Score);
					break;

					case 4: //*****STOP GAME (UNTESTED)*****
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
					case 2: //*****ACCEPTED TO JOIN SERVER (UNIMPLEMENTED)*****
						Debug.Log("Joining server.");
						Debug.Log("Team: " + msg.AcceptJoinMsg.Team);
						Debug.Log("Position: " + msg.AcceptJoinMsg.Position);
						Debug.Log("Rotation: " + msg.AcceptJoinMsg.Rotation);
						instance.JoinServer(msg.AcceptJoinMsg.Team, msg.AcceptJoinMsg.Position, msg.AcceptJoinMsg.Rotation);
					break;

					case 3: //*****NEW OTHER PLAYERS (UNIMPLEMENTED)*****
						Debug.Log("New player joined.");
						Debug.Log("ID: " + msg.NewPlayerMsg.Id);
						Debug.Log("Team: " + msg.NewPlayerMsg.Team);
						Debug.Log("Position: " + msg.NewPlayerMsg.Position);
						Debug.Log("Rotation: " + msg.NewPlayerMsg.Rotation);
						instance.NewOtherPlayer(msg.NewPlayerMsg.Id, msg.NewPlayerMsg.Team, msg.NewPlayerMsg.Position, msg.NewPlayerMsg.Rotation);
					break;

					case 4: //*****START GAME (UNTESTED)*****
						Globals.gameOn = true;
					break;
				}
			}
		}

		//CLIENT MESSAGE HANDLERS GAMEOFF

        private void JoinServer(uint team, Vector3 position, Quaternion rotation)
        {

        }

        private void NewOtherPlayer(string id, uint team, Vector3 position, Quaternion rotation)
        {

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
