using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Just a component that hold reference to another object on scene
/// </summary>
public class ReferenceHolder : MonoBehaviour {
	[SerializeField] GameObject gameobjectReference;
	[SerializeField] Rigidbody2D rigidbodyReference;
	[SerializeField] ReferenceHolder monobehaviorReference;
	[SerializeField] GameObject[] referenceArray;
}
