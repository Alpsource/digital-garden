using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace ImmersiveVideo
{
    // TASK-11: positions and shows/hides the world-space Start button, including the
    // lazy-follow behavior - the button doesn't chase the user's head every frame, but
    // if it drifts too far out of view for too long it smoothly recenters.
    public class IdleController : MonoBehaviour
    {
        [SerializeField] Transform m_HeadTransform;
        [SerializeField] GameObject m_Canvas;
        [SerializeField] Button m_Button;
        [SerializeField] Transform m_ButtonRoot;
        [SerializeField] ImmersiveVideoConfig m_Config;
        // Fallback trigger alongside the uGUI Button.onClick path - a plain 3D collider +
        // XRSimpleInteractable is the simplest, most basic XRI interaction primitive and
        // doesn't depend on EventSystem/XRUIInputModule/TrackedDeviceGraphicRaycaster at all.
        [SerializeField] XRSimpleInteractable m_SelectInteractable;
        // FIX-PLAN ADIM 4: input-device-independent fallback trigger path.
        [SerializeField] GazeDwellButton m_GazeDwellButton;

        public event Action StartRequested;

        Coroutine m_HideRoutine;
        Coroutine m_FollowRoutine;
        float m_OutOfConeTimer;
        Vector3 m_PositionVelocity;
        Vector3 m_RestingScale;

        void Awake()
        {
            // The setup script scales the canvas down so its RectTransform (hundreds of
            // "pixels") renders as buttonDiameter meters. That scale - NOT Vector3.one -
            // is the correct "visible" resting scale; hardcoding Vector3.one here used to
            // blow the button up to hundreds of meters wide every time Idle was entered.
            m_RestingScale = m_ButtonRoot != null ? m_ButtonRoot.localScale : Vector3.one;

            if (m_Canvas != null)
                m_Canvas.SetActive(false);
            if (m_Button != null)
                m_Button.onClick.AddListener(OnClicked);
            if (m_SelectInteractable != null)
                m_SelectInteractable.selectEntered.AddListener(OnSelectEntered);
            if (m_GazeDwellButton != null)
                m_GazeDwellButton.Activated += RequestStart;
        }

        void OnDestroy()
        {
            if (m_Button != null)
                m_Button.onClick.RemoveListener(OnClicked);
            if (m_SelectInteractable != null)
                m_SelectInteractable.selectEntered.RemoveListener(OnSelectEntered);
            if (m_GazeDwellButton != null)
                m_GazeDwellButton.Activated -= RequestStart;
        }

        void OnSelectEntered(SelectEnterEventArgs args) => RequestStart();

        void OnClicked() => RequestStart();

        public void RequestStart() => StartRequested?.Invoke();

        public void EnterIdle()
        {
            if (m_HideRoutine != null)
            {
                StopCoroutine(m_HideRoutine);
                m_HideRoutine = null;
            }

            m_Canvas.SetActive(true);
            m_ButtonRoot.localScale = m_RestingScale;
            m_OutOfConeTimer = 0f;
            PlaceAtGaze(m_ButtonRoot);

            if (m_FollowRoutine != null)
                StopCoroutine(m_FollowRoutine);
            m_FollowRoutine = StartCoroutine(FollowLoop());
        }

        public void ExitIdle()
        {
            if (m_FollowRoutine != null)
            {
                StopCoroutine(m_FollowRoutine);
                m_FollowRoutine = null;
            }
            if (m_HideRoutine != null)
                StopCoroutine(m_HideRoutine);
            m_HideRoutine = StartCoroutine(ShrinkAndHide());
        }

        IEnumerator ShrinkAndHide()
        {
            const float duration = 0.25f;
            var startScale = m_ButtonRoot.localScale;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                m_ButtonRoot.localScale = Vector3.LerpUnclamped(startScale, Vector3.zero, elapsed / duration);
                yield return null;
            }
            m_ButtonRoot.localScale = Vector3.zero;
            m_Canvas.SetActive(false);
        }

        void PlaceAtGaze(Transform target)
        {
            var pitch = Quaternion.AngleAxis(m_Config.buttonHeightOffsetDeg, m_HeadTransform.right);
            var direction = (pitch * m_HeadTransform.forward).normalized;
            target.position = m_HeadTransform.position + direction * m_Config.buttonDistance;
            target.rotation = Quaternion.LookRotation(target.position - m_HeadTransform.position);
        }

        IEnumerator FollowLoop()
        {
            while (true)
            {
                var toButton = (m_ButtonRoot.position - m_HeadTransform.position).normalized;
                var angle = Vector3.Angle(m_HeadTransform.forward, toButton);

                if (angle > m_Config.buttonRecenterAngle)
                {
                    m_OutOfConeTimer += Time.unscaledDeltaTime;
                    if (m_OutOfConeTimer >= m_Config.buttonRecenterDelay)
                    {
                        yield return RecenterToGaze();
                        m_OutOfConeTimer = 0f;
                    }
                }
                else
                {
                    m_OutOfConeTimer = 0f;
                }

                yield return null;
            }
        }

        IEnumerator RecenterToGaze()
        {
            var pitch = Quaternion.AngleAxis(m_Config.buttonHeightOffsetDeg, m_HeadTransform.right);
            var direction = (pitch * m_HeadTransform.forward).normalized;
            var targetPos = m_HeadTransform.position + direction * m_Config.buttonDistance;
            var targetRot = Quaternion.LookRotation(targetPos - m_HeadTransform.position);
            var startRot = m_ButtonRoot.rotation;

            var duration = Mathf.Max(m_Config.buttonRecenterSmooth, 0.01f);
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                m_ButtonRoot.position = Vector3.SmoothDamp(m_ButtonRoot.position, targetPos, ref m_PositionVelocity, duration);
                m_ButtonRoot.rotation = Quaternion.Slerp(startRot, targetRot, elapsed / duration);
                yield return null;
            }
            m_ButtonRoot.position = targetPos;
            m_ButtonRoot.rotation = targetRot;
        }
    }
}
