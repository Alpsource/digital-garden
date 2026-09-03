using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace ImmersiveVideo
{
    public enum HandChoice
    {
        LeftHand,
        RightHand
    }

    // TASK-11: drives a Near-Far Interactor's pose and UI-press input directly from the
    // OpenXR "Hand Interaction Profile" device (XR_EXT_hand_interaction), which ships in
    // the already-installed com.unity.xr.openxr package - no com.unity.xr.hands needed.
    // Reads the device's controls directly instead of authoring Input Action bindings,
    // using XRInputButtonReader's ManualValue mode. This is new, unverified territory -
    // confirm on-device that Quest 3 actually reports this device before trusting it.
    [DisallowMultipleComponent]
    public class HandInteractionDriver : MonoBehaviour
    {
        [SerializeField] HandChoice m_Hand = HandChoice.RightHand;
        [SerializeField] NearFarInteractor m_Interactor;

        const float k_PinchThreshold = 0.7f;

        HandInteractionProfile.HandInteraction m_Device;

        void Awake()
        {
            if (m_Interactor == null)
                return;
            m_Interactor.uiPressInput.inputSourceMode = XRInputButtonReader.InputSourceMode.ManualValue;
            // Also drive the plain 3D select input - the uGUI/EventSystem/XRUIInputModule
            // chain has several failure points; a 3D XRSimpleInteractable + selectInput is
            // the simplest, most basic XRI interaction primitive and a reliable fallback.
            m_Interactor.selectInput.inputSourceMode = XRInputButtonReader.InputSourceMode.ManualValue;
        }

        void Update()
        {
            if (m_Interactor == null)
                return;

            if (m_Device == null || !m_Device.added)
                m_Device = FindDevice();

            if (m_Device == null)
            {
                m_Interactor.enabled = false;
                return;
            }

            var pose = m_Device.pointer.ReadValue();
            if (!pose.isTracked)
            {
                m_Interactor.enabled = false;
                return;
            }

            m_Interactor.enabled = true;
            transform.SetPositionAndRotation(pose.position, pose.rotation);

            var pinch = m_Device.pinchValue.ReadValue();
            var pressed = pinch >= k_PinchThreshold ? 1f : 0f;
            m_Interactor.uiPressInput.manualValue = pressed;
            m_Interactor.selectInput.manualValue = pressed;
        }

        HandInteractionProfile.HandInteraction FindDevice()
        {
            var targetUsage = m_Hand == HandChoice.LeftHand ? CommonUsages.LeftHand : CommonUsages.RightHand;
            foreach (var device in InputSystem.devices)
            {
                if (device is HandInteractionProfile.HandInteraction hand && hand.usages.Contains(targetUsage))
                    return hand;
            }
            return null;
        }
    }
}
