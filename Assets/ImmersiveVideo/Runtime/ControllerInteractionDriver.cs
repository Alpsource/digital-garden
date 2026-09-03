using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace ImmersiveVideo
{
    // TASK-11 fix, take 2: the first version looked for the concrete
    // OculusTouchControllerProfile.OculusTouchController C# type. But "XRI Default Input
    // Actions" (now wired up via InputActionManager) binds through the GENERIC
    // <XRController>{Hand}/... layout, and UnityEngine.InputSystem.XR.XRController - the
    // generic base class - carries no strongly-typed control properties of its own (it's
    // just a marker layout). Whichever concrete type the OpenXR runtime actually
    // instantiates for the negotiated interaction profile, it should still be an
    // XRController (that's the base every controller layout extends), so match on that
    // generic type and pull controls by name instead of relying on the exact concrete type.
    [DisallowMultipleComponent]
    public class ControllerInteractionDriver : MonoBehaviour
    {
        [SerializeField] HandChoice m_Hand = HandChoice.RightHand;
        [SerializeField] NearFarInteractor m_Interactor;

        const float k_TriggerThreshold = 0.5f;

        UnityEngine.InputSystem.XR.XRController m_Device;
        Vector3Control m_PositionControl;
        QuaternionControl m_RotationControl;
        ButtonControl m_IsTrackedControl;
        AxisControl m_TriggerControl;

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
                FindDevice();

            if (m_Device == null || m_IsTrackedControl == null)
            {
                m_Interactor.enabled = false;
                return;
            }

            if (m_IsTrackedControl.ReadValue() < 0.5f)
            {
                m_Interactor.enabled = false;
                return;
            }

            m_Interactor.enabled = true;
            var position = m_PositionControl != null ? m_PositionControl.ReadValue() : Vector3.zero;
            var rotation = m_RotationControl != null ? m_RotationControl.ReadValue() : Quaternion.identity;
            transform.SetPositionAndRotation(position, rotation);

            var trigger = m_TriggerControl != null ? m_TriggerControl.ReadValue() : 0f;
            var pressed = trigger >= k_TriggerThreshold ? 1f : 0f;
            m_Interactor.uiPressInput.manualValue = pressed;
            m_Interactor.selectInput.manualValue = pressed;
        }

        void FindDevice()
        {
            var targetUsage = m_Hand == HandChoice.LeftHand ? CommonUsages.LeftHand : CommonUsages.RightHand;
            foreach (var device in InputSystem.devices)
            {
                if (device is not UnityEngine.InputSystem.XR.XRController controller || !controller.usages.Contains(targetUsage))
                    continue;

                m_Device = controller;
                m_PositionControl = controller.TryGetChildControl<Vector3Control>("pointerPosition")
                    ?? controller.TryGetChildControl<Vector3Control>("devicePosition");
                m_RotationControl = controller.TryGetChildControl<QuaternionControl>("pointerRotation")
                    ?? controller.TryGetChildControl<QuaternionControl>("deviceRotation");
                m_IsTrackedControl = controller.TryGetChildControl<ButtonControl>("isTracked");
                m_TriggerControl = controller.TryGetChildControl<AxisControl>("trigger");
                return;
            }
        }
    }
}
