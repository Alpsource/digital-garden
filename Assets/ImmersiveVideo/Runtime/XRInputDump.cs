using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.OpenXR;

namespace ImmersiveVideo
{
    // FIX-PLAN ADIM 1: one-shot (+periodic) dump of everything the runtime actually
    // negotiated/exposes, so we stop guessing which device type/layout shows up on
    // Quest 3 for controllers/hands and read it straight from logcat instead.
    public class XRInputDump : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [SerializeField] float m_IntervalSeconds = 5f;
        float m_Timer;

        void Start()
        {
            Dump();
        }

        void Update()
        {
            m_Timer += Time.unscaledDeltaTime;
            if (m_Timer < m_IntervalSeconds)
                return;
            m_Timer = 0f;
            Dump();
        }

        void Dump()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[ImmersiveVideo][XRInputDump] ===== dump start =====");

            sb.AppendLine($"[ImmersiveVideo][XRInputDump] OpenXRRuntime.name={OpenXRRuntime.name} version={OpenXRRuntime.version}");
            var extensions = OpenXRRuntime.GetEnabledExtensions();
            sb.AppendLine($"[ImmersiveVideo][XRInputDump] Enabled OpenXR extensions ({extensions.Length}):");
            foreach (var ext in extensions)
                sb.AppendLine($"[ImmersiveVideo][XRInputDump]   - {ext}");

            sb.AppendLine($"[ImmersiveVideo][XRInputDump] InputSystem.devices ({InputSystem.devices.Count}):");
            foreach (var device in InputSystem.devices)
            {
                var usages = string.Join(",", device.usages);
                sb.AppendLine($"[ImmersiveVideo][XRInputDump]   - type={device.GetType().FullName} layout={device.layout} " +
                    $"usages=[{usages}] added={device.added} enabled={device.enabled}");
                foreach (var control in device.allControls)
                    sb.AppendLine($"[ImmersiveVideo][XRInputDump]       control: {control.path} ({control.GetType().Name})");
            }

            var legacyDevices = new System.Collections.Generic.List<UnityEngine.XR.InputDevice>();
            UnityEngine.XR.InputDevices.GetDevices(legacyDevices);
            sb.AppendLine($"[ImmersiveVideo][XRInputDump] Legacy UnityEngine.XR.InputDevices ({legacyDevices.Count}):");
            foreach (var device in legacyDevices)
            {
                sb.AppendLine($"[ImmersiveVideo][XRInputDump]   - name={device.name} " +
                    $"characteristics={device.characteristics} valid={device.isValid}");
            }

            sb.AppendLine("[ImmersiveVideo][XRInputDump] ===== dump end =====");
            Debug.Log(sb.ToString());
        }
#endif
    }
}
