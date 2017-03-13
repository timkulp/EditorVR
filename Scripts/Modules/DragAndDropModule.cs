﻿#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace UnityEditor.Experimental.EditorVR.Modules
{
	sealed class DragAndDropModule : MonoBehaviour
	{
		readonly Dictionary<Transform, IDroppable> m_Droppables = new Dictionary<Transform, IDroppable>();
		readonly Dictionary<Transform, IDropReceiver> m_DropReceivers = new Dictionary<Transform, IDropReceiver>();

		readonly Dictionary<Transform, GameObject> m_HoverObjects = new Dictionary<Transform, GameObject>();

		void SetCurrentDroppable(Transform rayOrigin, IDroppable droppable)
		{
			if (droppable != null)
				m_Droppables[rayOrigin] = droppable;
		}

		object GetCurrentDropObject(Transform rayOrigin)
		{
			IDroppable droppable;
			return m_Droppables.TryGetValue(rayOrigin, out droppable) ? droppable.GetDropObject() : null;
		}

		void SetCurrentDropReceiver(Transform rayOrigin, IDropReceiver dropReceiver)
		{
			if (dropReceiver == null)
				m_DropReceivers.Remove(rayOrigin);
			else
				m_DropReceivers[rayOrigin] = dropReceiver;
		}

		public IDropReceiver GetCurrentDropReceiver(Transform rayOrigin)
		{
			IDropReceiver dropReceiver;
			if (m_DropReceivers.TryGetValue(rayOrigin, out dropReceiver))
				return dropReceiver;

			return null;
		}

		public void OnRayEntered(GameObject gameObject, RayEventData eventData)
		{
			var dropReceiver = gameObject.GetComponent<IDropReceiver>();
			if (dropReceiver != null)
			{
				if (dropReceiver.CanDrop(GetCurrentDropObject(eventData.rayOrigin)))
				{
					dropReceiver.OnDropHoverStarted();
					m_HoverObjects[eventData.rayOrigin] = gameObject;
					SetCurrentDropReceiver(eventData.rayOrigin, dropReceiver);
				}
			}
		}

		public void OnRayExited(GameObject gameObject, RayEventData eventData)
		{
			if (!gameObject)
				return;

			var dropReceiver = gameObject.GetComponent<IDropReceiver>();
			if (dropReceiver != null)
			{
				if (m_HoverObjects.Remove(eventData.rayOrigin))
				{
					dropReceiver.OnDropHoverEnded();
					SetCurrentDropReceiver(eventData.rayOrigin, null);
				}
			}
		}

		public void OnDragStarted(GameObject gameObject, RayEventData eventData)
		{
			var droppable = gameObject.GetComponent<IDroppable>();
			if (droppable != null)
				SetCurrentDroppable(eventData.rayOrigin, droppable);
		}

		public void OnDragEnded(GameObject gameObject, RayEventData eventData)
		{
			var droppable = gameObject.GetComponent<IDroppable>();
			if (droppable != null)
			{
				var rayOrigin = eventData.rayOrigin;
				SetCurrentDroppable(rayOrigin, null);

				var dropReceiver = GetCurrentDropReceiver(rayOrigin);
				var dropObject = droppable.GetDropObject();
				if (dropReceiver != null && dropReceiver.CanDrop(dropObject))
					dropReceiver.ReceiveDrop(droppable.GetDropObject());
			}
		}
	}
}
#endif
