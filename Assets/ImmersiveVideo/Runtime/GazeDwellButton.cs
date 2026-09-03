using System;
using UnityEngine;

namespace ImmersiveVideo
{
    // FIX-PLAN ADIM 4: gaze-dwell fallback trigger. Deliberately independent of the
    // Input System / XRI / EventSystem chain - it only needs Camera.main and a collider,
    // so it keeps working (and keeps testing unblocked) even if controller/hand input
    // breaks again. Meant to be kept permanently as a third trigger path alongside the
    // uGUI Button and the controller/hand interactors, not just for debugging.
    [RequireComponent(typeof(Collider))]
    public class GazeDwellButton : MonoBehaviour
    {
        [SerializeField] float m_DwellSeconds = 1.5f;
        [SerializeField] float m_MaxDistance = 10f;

        public event Action Activated;

        Collider m_Collider;
        float m_DwellTimer;
        bool m_Fired;

        void Awake()
        {
            m_Collider = GetComponent<Collider>();
        }

        void OnEnable()
        {
            m_DwellTimer = 0f;
            m_Fired = false;
        }

        void Update()
        {
            var cam = Camera.main;
            if (cam == null)
                return;

            var ray = new Ray(cam.transform.position, cam.transform.forward);
            var isGazing = m_Collider.Raycast(ray, out _, m_MaxDistance);

            if (!isGazing)
            {
                m_DwellTimer = 0f;
                m_Fired = false;
                return;
            }

            if (m_Fired)
                return;

            m_DwellTimer += Time.unscaledDeltaTime;
            if (m_DwellTimer >= m_DwellSeconds)
            {
                m_Fired = true;
                Activated?.Invoke();
            }
        }
    }
}
