#if UNITY_EDITOR && UNITY_EDITORVR
using System;
using System.Collections;
using UnityEditor.Experimental.EditorVR.Helpers;
using UnityEditor.Experimental.EditorVR.Modules;
using UnityEditor.Experimental.EditorVR.Utilities;
using UnityEngine;
using UnityEngine.VR;

namespace UnityEditor.Experimental.EditorVR.Core
{
	partial class EditorVR
	{
		[SerializeField]
		GameObject m_PlayerModelPrefab;

		[SerializeField]
		GameObject m_PreviewCameraPrefab;

		class Viewer : Nested, IInterfaceConnector, ISerializePreferences
		{
			[Serializable]
			class Preferences
			{
				[SerializeField]
				Vector3 m_CameraPosition;
				[SerializeField]
				Quaternion m_CameraRotation;
				[SerializeField]
				float m_CameraRigScale = 1;

				public Vector3 cameraPosition { get { return m_CameraPosition; } set { m_CameraPosition = value; } }
				public Quaternion cameraRotation { get { return m_CameraRotation; } set { m_CameraRotation = value; } }
				public float cameraRigScale { get { return m_CameraRigScale; } set { m_CameraRigScale = value; } }
			}

			const float k_CameraRigTransitionTime = 0.75f;

			PlayerBody m_PlayerBody;
			float m_OriginalNearClipPlane;
			float m_OriginalFarClipPlane;

			readonly Preferences m_Preferences = new Preferences();

			internal IPreviewCamera customPreviewCamera { get; private set; }

			public bool preserveCameraRig { private get; set; }

			public bool hmdReady { get; private set; }

			public Viewer()
			{
				IMoveCameraRigMethods.moveCameraRig = MoveCameraRig;
				IUsesViewerBodyMethods.isOverShoulder = IsOverShoulder;
				IUsesViewerBodyMethods.isAboveHead = IsAboveHead;
				IUsesViewerScaleMethods.getViewerScale = GetViewerScale;
				IUsesViewerScaleMethods.setViewerScale = SetViewerScale;

				VRView.hmdStatusChange += OnHMDStatusChange;

				preserveCameraRig = true;
			}

			internal override void OnDestroy()
			{
				base.OnDestroy();

				VRView.hmdStatusChange -= OnHMDStatusChange;

				var cameraRig = CameraUtils.GetCameraRig();
				cameraRig.transform.parent = null;

				ObjectUtils.Destroy(m_PlayerBody.gameObject);

				if (customPreviewCamera != null)
					ObjectUtils.Destroy(((MonoBehaviour)customPreviewCamera).gameObject);
			}

			public void ConnectInterface(object obj, Transform rayOrigin = null)
			{
				var locomotion = obj as ILocomotor;
				if (locomotion != null)
					locomotion.cameraRig = VRView.cameraRig;

				var usesCameraRig = obj as IUsesCameraRig;
				if (usesCameraRig != null)
					usesCameraRig.cameraRig = CameraUtils.GetCameraRig();
			}

			public void DisconnectInterface(object obj)
			{
			}

			public object OnSerializePreferences()
			{
				if (!preserveCameraRig)
					return null;

				if (hmdReady)
					SaveCameraState();

				return m_Preferences;
			}

			void OnHMDStatusChange(bool ready)
			{
				hmdReady = ready;
				if (!ready)
					SaveCameraState();
			}

			void SaveCameraState()
			{
				var camera = CameraUtils.GetMainCamera();
				var cameraRig = CameraUtils.GetCameraRig();
				var cameraTransform = camera.transform;
				var cameraRigScale = cameraRig.localScale.x;
				m_Preferences.cameraRigScale = cameraRigScale;
				m_Preferences.cameraPosition = cameraTransform.position;
				m_Preferences.cameraRotation = MathUtilsExt.ConstrainYawRotation(cameraTransform.rotation);
			}

			public void OnDeserializePreferences(object obj)
			{
				if (!preserveCameraRig)
					return;

				var preferences = (Preferences)obj;

				var camera = CameraUtils.GetMainCamera();
				var cameraRig = CameraUtils.GetCameraRig();
				var cameraTransform = camera.transform;
				var cameraRotation = MathUtilsExt.ConstrainYawRotation(cameraTransform.rotation);
				var inverseRotation = Quaternion.Inverse(cameraRotation);
				cameraRig.position = Vector3.zero;
				cameraRig.rotation = inverseRotation * preferences.cameraRotation;
				SetViewerScale(preferences.cameraRigScale);
				cameraRig.position = preferences.cameraPosition - cameraTransform.position;
			}

			internal void InitializeCamera()
			{
				var cameraRig = CameraUtils.GetCameraRig();
				cameraRig.parent = evr.transform; // Parent the camera rig under EditorVR
				cameraRig.hideFlags = defaultHideFlags;
				var viewerCamera = CameraUtils.GetMainCamera();
				viewerCamera.gameObject.hideFlags = defaultHideFlags;
				m_OriginalNearClipPlane = viewerCamera.nearClipPlane;
				m_OriginalFarClipPlane = viewerCamera.farClipPlane;
				if (VRSettings.loadedDeviceName == "OpenVR")
				{
					// Steam's reference position should be at the feet and not at the head as we do with Oculus
					cameraRig.localPosition = Vector3.zero;
				}

				var hmdOnlyLayerMask = 0;
				if (evr.m_PreviewCameraPrefab)
				{
					var go = ObjectUtils.Instantiate(evr.m_PreviewCameraPrefab);

					customPreviewCamera = go.GetComponentInChildren<IPreviewCamera>();
					if (customPreviewCamera != null)
					{
						VRView.customPreviewCamera = customPreviewCamera.previewCamera;
						customPreviewCamera.vrCamera = VRView.viewerCamera;
						hmdOnlyLayerMask = customPreviewCamera.hmdOnlyLayerMask;
						evr.m_Interfaces.ConnectInterfaces(customPreviewCamera);
					}
				}
				VRView.cullingMask = UnityEditor.Tools.visibleLayers | hmdOnlyLayerMask;
			}

			internal void UpdateCamera()
			{
				if (customPreviewCamera != null)
					customPreviewCamera.enabled = VRView.showDeviceView && VRView.customPreviewCamera != null;
			}

			internal void AddPlayerModel()
			{
				m_PlayerBody = ObjectUtils.Instantiate(evr.m_PlayerModelPrefab, CameraUtils.GetMainCamera().transform, false).GetComponent<PlayerBody>();
				var renderer = m_PlayerBody.GetComponent<Renderer>();
				evr.GetModule<SpatialHashModule>().spatialHash.AddObject(renderer, renderer.bounds);
				evr.GetModule<SnappingModule>().ignoreList = renderer.GetComponentsInChildren<Renderer>(true);
			}

			internal bool IsOverShoulder(Transform rayOrigin)
			{
				return Overlaps(rayOrigin, m_PlayerBody.overShoulderTrigger);
			}

			bool IsAboveHead(Transform rayOrigin)
			{
				return Overlaps(rayOrigin, m_PlayerBody.aboveHeadTrigger);
			}

			static bool Overlaps(Transform rayOrigin, Collider trigger)
			{
				var radius = DirectSelection.GetPointerLength(rayOrigin);

				var colliders = Physics.OverlapSphere(rayOrigin.position, radius, -1, QueryTriggerInteraction.Collide);
				foreach (var collider in colliders)
				{
					if (collider == trigger)
						return true;
				}

				return false;
			}

			internal static void DropPlayerHead(Transform playerHead)
			{
				var cameraRig = CameraUtils.GetCameraRig();
				var mainCamera = CameraUtils.GetMainCamera().transform;

				// Hide player head to avoid jarring impact
				var playerHeadRenderers = playerHead.GetComponentsInChildren<Renderer>();
				foreach (var renderer in playerHeadRenderers)
				{
					renderer.enabled = false;
				}

				var rotationDiff = MathUtilsExt.ConstrainYawRotation(Quaternion.Inverse(mainCamera.rotation) * playerHead.rotation);
				var cameraDiff = cameraRig.position - mainCamera.position;
				cameraDiff.y = 0;
				var rotationOffset = rotationDiff * cameraDiff - cameraDiff;

				var endPosition = cameraRig.position + (playerHead.position - mainCamera.position) + rotationOffset;
				var endRotation = cameraRig.rotation * rotationDiff;
				var viewDirection = endRotation * Vector3.forward;

				evr.StartCoroutine(UpdateCameraRig(endPosition, viewDirection, () =>
				{
					playerHead.hideFlags = defaultHideFlags;
					playerHead.parent = mainCamera;
					playerHead.localRotation = Quaternion.identity;
					playerHead.localPosition = Vector3.zero;

					foreach (var renderer in playerHeadRenderers)
					{
						renderer.enabled = true;
					}
				}));
			}

			static IEnumerator UpdateCameraRig(Vector3 position, Vector3? viewDirection, Action onComplete = null)
			{
				var cameraRig = CameraUtils.GetCameraRig();

				var startPosition = cameraRig.position;
				var startRotation = cameraRig.rotation;

				var rotation = startRotation;
				if (viewDirection.HasValue)
				{
					var direction = viewDirection.Value;
					direction.y = 0;
					rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
				}

				var diffTime = 0f;
				var startTime = Time.realtimeSinceStartup;
				while (diffTime < k_CameraRigTransitionTime)
				{
					var t = diffTime / k_CameraRigTransitionTime;
					// Use a Lerp instead of SmoothDamp for constant velocity (avoid motion sickness)
					cameraRig.position = Vector3.Lerp(startPosition, position, t);
					cameraRig.rotation = Quaternion.Lerp(startRotation, rotation, t);
					yield return null;
					diffTime = Time.realtimeSinceStartup - startTime;
				}

				cameraRig.position = position;
				cameraRig.rotation = rotation;

				if (onComplete != null)
					onComplete();
			}

			static void MoveCameraRig(Vector3 position, Vector3? viewdirection)
			{
				evr.StartCoroutine(UpdateCameraRig(position, viewdirection));
			}

			internal static float GetViewerScale()
			{
				return CameraUtils.GetCameraRig().localScale.x;
			}

			void SetViewerScale(float scale)
			{
				var camera = CameraUtils.GetMainCamera();
				CameraUtils.GetCameraRig().localScale = Vector3.one * scale;
				camera.nearClipPlane = m_OriginalNearClipPlane * scale;
				camera.farClipPlane = m_OriginalFarClipPlane * scale;
			}
		}
	}
}
#endif
