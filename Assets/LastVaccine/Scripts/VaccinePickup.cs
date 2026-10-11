using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LastVaccine
{
    [RequireComponent(typeof(Collider))]
    public sealed class VaccinePickup : MonoBehaviour
    {
        public float interactDistance = 2.2f;
        Transform player;
        void OnEnable() { player = GameObject.FindGameObjectWithTag("Player")?.transform; }
        void Update()
        {
            transform.Rotate(0f, 65f * Time.deltaTime, 0f, Space.World);
            if (player != null && Vector3.Distance(transform.position, player.position) <= interactDistance && InteractPressed()) LastVaccineGame.Instance?.Victory();
        }
        void OnGUI()
        {
            if (player != null && Vector3.Distance(transform.position, player.position) <= interactDistance)
            {
                GUIStyle style = new GUIStyle(GUI.skin.box) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                GUI.Box(new Rect(Screen.width * .5f - 175, Screen.height - 110, 350, 52), "PRESS E TO COLLECT VACCINE", style);
            }
        }
        static bool InteractPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.E);
#endif
        }
    }
}
