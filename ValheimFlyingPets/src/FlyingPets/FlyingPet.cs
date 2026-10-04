using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace FlyingPets
{
    /// <summary>
    ///     A summoned winged mount. Networking follows the vanilla saddle: whoever rides the pet owns
    ///     its ZDO and simulates it; everybody else reads the state from the ZDO and animates locally.
    ///     The rider is attached to <see cref="m_seat" />, which the boarding sequence moves along the
    ///     lowered wing into the saddle.
    /// </summary>
    public partial class FlyingPet : MonoBehaviour, Hoverable, Interactable, IDoodadController
    {
        // state shared through the ZDO
        internal const int StateGround = 0;
        internal const int StateAir = 1;
        internal const int StateKneel = 2;

        internal const int SeqNone = 0;
        internal const int SeqMount = 1;
        internal const int SeqDismount = 2;

        // boarding timeline, seconds
        internal const float TKneel = 0.6f;      // knees down, near wing lowered to the ground
        internal const float THop = 0.95f;       // mount: rider has stepped onto the wing
        internal const float TLift = 1.85f;      // mount: the wing has lifted the rider into the saddle
        internal const float TDropWing = 1.5f;   // dismount: the wing is down again
        internal const float TOff = 1.85f;       // dismount: rider steps off onto the ground
        internal const float TStand = 2.45f;     // standing again

        internal static string RideAnimation = "attach_lox";
        internal static string ChairAnimation = "attach_chair";

        // set when the prefab is built (serialized, copied by Instantiate)
        public string m_petKey = "";
        public Transform m_visual;
        public Transform[] m_bones;
        public Transform m_seat;
        public Light m_eyeLight;
        public Vector3 m_capsuleCenter;
        public float m_capsuleRadius;
        public float m_capsuleHeight;
        public float m_eyeLightRange;

        internal static readonly List<FlyingPet> Instances = new List<FlyingPet>();

        private static readonly int ZState = "fp_state".GetStableHashCode();
        private static readonly int ZRider = "fp_rider".GetStableHashCode();
        private static readonly int ZOwner = "fp_owner".GetStableHashCode();
        private static readonly int ZOwnerName = "fp_ownername".GetStableHashCode();
        private static readonly int ZShare = "fp_share".GetStableHashCode();
        private static readonly int ZScale = "fp_scale".GetStableHashCode();
        private static readonly int ZSeq = "fp_seq".GetStableHashCode();
        private static readonly int ZSeqId = "fp_seqid".GetStableHashCode();
        private static readonly int ZSeqSide = "fp_seqside".GetStableHashCode();
        private static readonly int ZBye = "fp_bye".GetStableHashCode();

        private ZNetView m_nview;
        private Rigidbody m_body;
        private CapsuleCollider m_capsule;
        private PetAsset m_asset;
        private PetAnimator m_anim;
        private Vector3[] m_restLocal;
        private Vector3 m_seatRest;
        private float m_scale = 1f;
        private bool m_ready;
        private bool m_destroyed;

        // what the network says (read every frame on every client)
        private int m_netState;
        private long m_netRider;
        private int m_seq;
        private int m_seqId = -1;
        private int m_seqSide = PetAnimator.Left;
        private float m_seqTime;

        // despawn
        private bool m_byeShown;
        private float m_byeTimer = -1f;

        internal float Scale
        {
            get { return m_scale; }
        }

        internal string DisplayName
        {
            get { return m_asset != null ? m_asset.DisplayName : name; }
        }

        internal long OwnerId
        {
            get { return m_nview != null && m_nview.IsValid() ? m_nview.GetZDO().GetLong(ZOwner, 0L) : 0L; }
        }

        internal long RiderId
        {
            get { return m_nview != null && m_nview.IsValid() ? m_nview.GetZDO().GetLong(ZRider, 0L) : 0L; }
        }

        // ------------------------------------------------------------------ lifecycle
        private void Awake()
        {
            m_nview = GetComponent<ZNetView>();
            m_body = GetComponent<Rigidbody>();
            m_capsule = GetComponent<CapsuleCollider>();
            m_asset = PetLibrary.Get(m_petKey);
            if (m_nview == null || m_nview.GetZDO() == null || m_asset == null || m_bones == null ||
                m_bones.Length != m_asset.BoneNames.Length || m_seat == null)
            {
                enabled = false;
                return;
            }

            m_anim = new PetAnimator(m_asset);
            InitAnimation();
            m_restLocal = new Vector3[m_bones.Length];
            for (int i = 0; i < m_bones.Length; i++)
            {
                m_restLocal[i] = m_bones[i].localPosition;
            }

            m_seatRest = m_seat.localPosition;
            ApplyScale(m_nview.GetZDO().GetFloat(ZScale, 1f));

            m_nview.Register<long, int>("FP_RequestMount", RPC_RequestMount);
            m_nview.Register<bool, int>("FP_MountReply", RPC_MountReply);
            m_nview.Register<long>("FP_Release", RPC_Release);
            m_nview.Register("FP_Dismiss", RPC_Dismiss);
            m_nview.Register<long>("FP_Call", RPC_Call);

            m_yaw = transform.eulerAngles.y;
            m_state = m_nview.GetZDO().GetInt(ZState, StateGround);
            m_lastPos = transform.position;
            m_lastSimPos = transform.position;
            m_t = Random.Range(0f, 30f);
            if (m_eyeLight != null)
            {
                m_eyeLight.enabled = ModConfig.EyeLight.Value;
            }

            m_ready = true;
            Instances.Add(this);
        }

        private void OnDestroy()
        {
            m_destroyed = true;
            Instances.Remove(this);
            if (m_localRider != null)
            {
                ForceReleaseLocal(true);
            }
        }

        /// <summary>Called right after the summoning player instantiates the pet.</summary>
        internal void InitSummoned(Player player)
        {
            var zdo = m_nview.GetZDO();
            zdo.Set(ZOwner, player.GetPlayerID());
            zdo.Set(ZOwnerName, player.GetPlayerName());
            zdo.Set(ZShare, ModConfig.OthersCanRide.Value);
            zdo.Set(ZScale, ModConfig.PetScale.Value);
            zdo.Set(ZState, StateGround);
            ApplyScale(ModConfig.PetScale.Value);
            m_state = StateGround;
        }

        private void ApplyScale(float s)
        {
            m_scale = Mathf.Clamp(s, 0.3f, 3f);
            if (m_visual != null)
            {
                m_visual.localScale = Vector3.one * m_scale;
            }

            if (m_capsule != null)
            {
                m_capsule.center = m_capsuleCenter * m_scale;
                m_capsule.radius = m_capsuleRadius * m_scale;
                m_capsule.height = m_capsuleHeight * m_scale;
            }

            if (m_eyeLight != null)
            {
                m_eyeLight.range = m_eyeLightRange * m_scale;
            }
        }

        internal static FlyingPet FindOwnedBy(long playerId)
        {
            foreach (var pet in Instances)
            {
                if (pet != null && pet.m_ready && !pet.m_byeShown && pet.m_nview.IsValid() && pet.OwnerId == playerId)
                {
                    return pet;
                }
            }

            return null;
        }

        private void Update()
        {
            if (!m_ready || !m_nview.IsValid())
            {
                return;
            }

            ReadNet();
            if (m_seq != SeqNone)
            {
                m_seqTime += Time.deltaTime;
            }

            UpdateLocalRider();
            UpdateBye();
        }

        private void ReadNet()
        {
            var zdo = m_nview.GetZDO();
            m_netState = zdo.GetInt(ZState, StateGround);
            m_netRider = zdo.GetLong(ZRider, 0L);
            int id = zdo.GetInt(ZSeqId, 0);
            if (id != m_seqId)
            {
                m_seqId = id;
                m_seq = zdo.GetInt(ZSeq, SeqNone);
                m_seqSide = zdo.GetInt(ZSeqSide, PetAnimator.Left);
                m_seqTime = 0f;
                m_dropSet = false;
            }
        }

        /// <summary>Owner only: start a boarding sequence on every client.</summary>
        private void StartSeq(int kind, int side)
        {
            var zdo = m_nview.GetZDO();
            int id = zdo.GetInt(ZSeqId, 0) + 1;
            zdo.Set(ZSeq, kind);
            zdo.Set(ZSeqSide, side);
            zdo.Set(ZSeqId, id);
            m_seq = kind;
            m_seqSide = side;
            m_seqId = id;
            m_seqTime = 0f;
            m_dropSet = false;
        }

        private void EndSeq()
        {
            var zdo = m_nview.GetZDO();
            int id = zdo.GetInt(ZSeqId, 0) + 1;
            zdo.Set(ZSeq, SeqNone);
            zdo.Set(ZSeqId, id);
            m_seq = SeqNone;
            m_seqId = id;
            m_seqTime = 0f;
        }

        // ------------------------------------------------------------------ dismiss / call
        /// <summary>Send the pet away (with a puff on every client).</summary>
        internal void Dismiss()
        {
            if (!m_ready || !m_nview.IsValid() || m_localRider != null)
            {
                return;
            }

            if (m_nview.IsOwner())
            {
                BeginBye();
            }
            else
            {
                m_nview.InvokeRPC("FP_Dismiss");
            }
        }

        /// <summary>Ask the pet to come to the player (it is moved next to them when far away).</summary>
        internal void Call(Player player)
        {
            m_nview.InvokeRPC("FP_Call", player.GetPlayerID());
        }

        private void BeginBye()
        {
            var zdo = m_nview.GetZDO();
            if (zdo.GetBool(ZBye, false))
            {
                return;
            }

            if (zdo.GetLong(ZRider, 0L) != 0L)
            {
                return; // never pull the pet from under somebody
            }

            zdo.Set(ZBye, true);
            m_byeTimer = 0.4f; // let the flag reach the other players so they see the puff too
        }

        private void UpdateBye()
        {
            if (!m_byeShown && m_nview.GetZDO().GetBool(ZBye, false))
            {
                m_byeShown = true;
                Util.SpawnEffect(Util.DespawnEffects, transform.position + Vector3.up * (1.4f * m_scale), Quaternion.identity);
                if (m_visual != null)
                {
                    m_visual.gameObject.SetActive(false);
                }
            }

            if (m_byeTimer >= 0f && m_nview.IsOwner())
            {
                m_byeTimer -= Time.deltaTime;
                if (m_byeTimer < 0f)
                {
                    m_nview.Destroy();
                }
            }
        }

        private void RPC_Dismiss(long sender)
        {
            if (m_nview.IsOwner())
            {
                BeginBye();
            }
        }

        private void RPC_Call(long sender, long playerId)
        {
            if (!m_nview.IsOwner() || m_nview.GetZDO().GetLong(ZRider, 0L) != 0L)
            {
                return;
            }

            var player = Util.FindPlayer(playerId);
            if (player == null)
            {
                return;
            }

            float far = Mathf.Max(40f, ModConfig.TeleportDistance.Value * 0.35f);
            if (Vector3.Distance(player.transform.position, transform.position) > far)
            {
                TeleportNear(player);
            }

            m_callTimer = 20f;
        }

        // ------------------------------------------------------------------ hover / interact
        public string GetHoverName()
        {
            return DisplayName;
        }

        public float GetHoverOffset()
        {
            return 0f;
        }

        public string GetHoverText()
        {
            if (!m_ready || !m_nview.IsValid() || m_byeShown)
            {
                return "";
            }

            var sb = new StringBuilder(DisplayName);
            string ownerName = m_nview.GetZDO().GetString(ZOwnerName, "");
            if (!string.IsNullOrEmpty(ownerName))
            {
                sb.Append("\n<size=80%>").Append(Texts.Get("fp_hover_owner", ownerName)).Append("</size>");
            }

            var player = Player.m_localPlayer;
            if (RiderId != 0L)
            {
                sb.Append("\n").Append(Texts.Get("fp_hover_busy"));
            }
            else if (player != null && CanRide(player))
            {
                sb.Append("\n[<color=yellow><b>$KEY_Use</b></color>] ").Append(Texts.Get("fp_hover_ride"));
            }

            if (player != null && OwnerId == player.GetPlayerID() && RiderId == 0L)
            {
                sb.Append("\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] ").Append(Texts.Get("fp_hover_release"));
            }

            return Localization.instance.Localize(sb.ToString());
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !m_ready || !m_nview.IsValid() || m_byeShown)
            {
                return false;
            }

            var player = user as Player;
            if (player == null || player != Player.m_localPlayer || player.IsDead())
            {
                return false;
            }

            if (alt)
            {
                if (OwnerId == player.GetPlayerID() && RiderId == 0L)
                {
                    Dismiss();
                    player.Message(MessageHud.MessageType.Center, Texts.Get("fp_msg_dismiss", DisplayName));
                    return true;
                }

                return false;
            }

            if (RiderId != 0L)
            {
                player.Message(MessageHud.MessageType.Center, Texts.Get("fp_msg_busy"));
                return false;
            }

            if (!CanRide(player))
            {
                player.Message(MessageHud.MessageType.Center, Texts.Get("fp_msg_notyours"));
                return false;
            }

            if (player.IsAttached() || player.GetDoodadController() != null)
            {
                return false;
            }

            Vector3 local = transform.InverseTransformPoint(player.transform.position);
            int side = local.x >= 0f ? PetAnimator.Right : PetAnimator.Left;
            m_nview.InvokeRPC("FP_RequestMount", player.GetPlayerID(), side);
            return false;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            return false;
        }

        private bool CanRide(Player player)
        {
            long owner = OwnerId;
            return owner == 0L || owner == player.GetPlayerID() || m_nview.GetZDO().GetBool(ZShare, false);
        }

        // ------------------------------------------------------------------ mount handshake (like the vanilla saddle)
        private void RPC_RequestMount(long sender, long playerId, int side)
        {
            if (!m_nview.IsOwner())
            {
                return;
            }

            var zdo = m_nview.GetZDO();
            long owner = zdo.GetLong(ZOwner, 0L);
            bool allowed = owner == 0L || owner == playerId || zdo.GetBool(ZShare, false);
            bool free = zdo.GetLong(ZRider, 0L) == 0L && m_seq == SeqNone && !zdo.GetBool(ZBye, false);
            if (m_state == StateAir)
            {
                m_landTimer = 6f; // come down first; the player presses E again
                free = false;
            }

            bool ok = allowed && free;
            if (ok)
            {
                zdo.Set(ZRider, playerId);
                zdo.SetOwner(sender);
            }

            m_nview.InvokeRPC(sender, "FP_MountReply", ok, side);
        }

        private void RPC_MountReply(long sender, bool granted, int side)
        {
            var player = Player.m_localPlayer;
            if (player == null)
            {
                return;
            }

            if (!granted)
            {
                if (m_netState != StateAir)
                {
                    player.Message(MessageHud.MessageType.Center, Texts.Get("fp_msg_busy"));
                }

                return;
            }

            BeginLocalMount(player, side);
        }

        private void RPC_Release(long sender, long playerId)
        {
            if (!m_nview.IsOwner())
            {
                return;
            }

            var zdo = m_nview.GetZDO();
            if (zdo.GetLong(ZRider, 0L) == playerId)
            {
                zdo.Set(ZRider, 0L);
            }
        }

        // ------------------------------------------------------------------ IDoodadController
        public void ApplyControlls(Vector3 moveDir, Vector3 lookDir, bool run, bool autoRun, bool block)
        {
            m_inMove = moveDir;
            m_inLook = lookDir;
            m_inRun = run;
            m_inAuto = autoRun;
            m_inBlock = block;
            m_inTime = Time.time;
        }

        public Component GetControlledComponent()
        {
            return this;
        }

        public Vector3 GetPosition()
        {
            return m_seat != null ? m_seat.position : transform.position;
        }

        public bool IsValid()
        {
            return !m_destroyed && this != null && m_ready && m_nview != null && m_nview.IsValid();
        }

        public void OnUseStop(Player player)
        {
            // the game takes the controls away: either our own dismount finished or something forced it
            if (player != null && player == m_localRider)
            {
                ReleaseLocal(player, false);
            }
        }
    }
}
