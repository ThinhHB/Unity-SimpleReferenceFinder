using UnityEngine;
using System.Collections;
using UnityEditor;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEditor.Experimental.SceneManagement;

/// <summary>
/// Main class (and also the main class of this tool :v).
/// Remember to put it in any Editor foldoer to let it work.
/// Follow the Readme or decription in Sample scene to know how to use this tool.
/// </summary>
public class ReferenceFinder : EditorWindow {
	[MenuItem("CONTEXT/Transform/Who reference this GameObject?")]
	static void SearchGameObjectReferenceInScene(MenuCommand command) {
		ClearConsole();
		var parentGameObject = ((Component)command.context).gameObject;
		Debug.LogFormat(parentGameObject, "----- Find GameObject reference : {0}", parentGameObject.name);
		FindObjectReferenceInAllActiveScene(parentGameObject);
	}

	[MenuItem("CONTEXT/Component/Who reference this Component?")]
	static void SearchComponentRefenreceInScene(MenuCommand command) {
		Debug.LogFormat(command.context, "----- Find Component reference : {0}", command.context.name);
		FindObjectReferenceInAllActiveScene(command.context);
	}

	[MenuItem("FindReference/Search Object References in active scene")]
	static void ObjectSearchReferenceInScene() {
		// the Object user selected in any window
		var selectedObject = Selection.activeObject;
		if (selectedObject == null) {
			Debug.LogWarning("You must select an object !!");
			return;
		}
		ClearConsole();
		Debug.LogFormat(selectedObject, "----- Find GameObject reference : {0}", selectedObject.name);
		FindObjectReferenceInAllActiveScene(selectedObject);
	}

	[MenuItem("Tools/Clear Console")]// %#c")] // CMD + SHIFT + C
	static void ClearConsole() {
		// This simply does "LogEntries.Clear()" the long way:
		const string LOG_ENTRIES_PATH = "UnityEditor.LogEntries, UnityEditor.dll";
		var logEntries = System.Type.GetType(LOG_ENTRIES_PATH);
		if (logEntries == null) {
			Debug.LogWarning("ReferenceFinder, cannot clear console windows");
			return;
		}
		var clearMethod = logEntries.GetMethod("Clear", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
		if (clearMethod == null) {
			Debug.LogWarning("ReferenceFinder, cannot find [Clear] method in dll to invoke, console not clear");
			return;
		}
		clearMethod.Invoke(null, null);
	}

	static void FindObjectReferenceInAllActiveScene(Object suspect) {
		// from Unity 2017, we have multiple scene in Hierachy
		var rootObjectList = new List<GameObject>();
		var allScene = new List<Scene>();
		// add all scene
		for (int n = 0; n < EditorSceneManager.sceneCount; n++) {
			var scene = EditorSceneManager.GetSceneAt(n);
			allScene.Add(scene);
		}
		// from 2019, we have prefab preview scene as well
		var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
		if(prefabStage != null) {
			var prefabScene = prefabStage.scene;
			allScene.Add(prefabScene);
		}
		// also find object in DontDestroyScene.
		// follow this post here : https://gamedev.stackexchange.com/questions/140014/how-can-i-get-all-dontdestroyonload-gameobjects
		// NOTES : access DontDestroyOnLoad scene only work in Editor. And DontDestroyScene only available in Play mode
		if (Application.isPlaying) {
			var decoyObject = new GameObject();
			GameObject.DontDestroyOnLoad(decoyObject);
			allScene.Add(decoyObject.scene);
			DestroyImmediate(decoyObject);
		}
		// expand our rootObjectList
		for (int n = 0; n < allScene.Count; n++) {
			var scene = allScene[n];
			if(!scene.isLoaded) {
				Debug.Log($"Scene [{scene.name}] exist in Hierarchy but not loaded, ignore search");
				continue;
			}
			var roots = new List<GameObject>();
			scene.GetRootGameObjects(roots);
			rootObjectList.AddRange(roots);
		}
		/// Use a loop throught rootObjects.GetComponentsInChildRen(true) in the active scene instead of :
		/// Object.FindObjectsOfType : only search in ACTIVE objects in scene
		/// Resources.FindObjectsOfTypeAll : too slow, cause it find in everything in our project
		for (int n = 0, amount = rootObjectList.Count; n < amount; n++) {
			FindReferenceInGameObjectAndItsChilds(rootObjectList[n], suspect);
		}
		Debug.Log("------ Search Finished ---------");
	}

	static void FindReferenceInGameObjectAndItsChilds(GameObject target, Object suspect) {
		/// DONT use GetComponentsInChildren() !!! If you have a nested gameobject with too many child levels,
		/// then this function may consume a lot of memory and cause Unity to crashed.
		/// SO, find reference in gameobject components first, then recursive call to all it child.
		/// Remeber to release any allocated List, array ... cause the recursive will put all current objects to stack,
		/// then our memory will keep expand on each recursive calls. Mind that to save your Unity :v

		// find in its components first.
		var componentList = new List<Component>();
		target.GetComponents<Component>(componentList);
		for (int n = 0, count = componentList.Count; n < count; n++) {
			var component = componentList[n];
			// remember this null checking step
			if (component == null)
				continue;
			/// use SerializeObject, so we can iterate through all serialized field in the component
			var serializeObject = new SerializedObject(component);
			var iterator = serializeObject.GetIterator();

			while (iterator.NextVisible(true)) {
				/// Here we'll checking 2 cases:
				/// Array : will iterator through that array
				/// Normal property : no iterator, just compare.
				/// In those cases, we have difference Log command
				/// NOTES : if the SerializeProperty.type == "string", then it's isArray also = true
				/// we should ignore this case, it cause "operation is not possible when moved past all properties" exception
				const string EXCLUDE_STRING_TYPE = "string";
				if (iterator.isArray && iterator.type != EXCLUDE_STRING_TYPE) {
					FindReferenceInArraySerializeProperty(iterator, suspect, component);
				}
				else {
					const string FORMAT = "object[{0}], component[{1}], field [{2}], scene [{3}]";
					if (CompareReferenceOfSerializeProperty(iterator, suspect)) {
						var sceneName = component.gameObject.scene.name;
						Debug.LogFormat(component, FORMAT, component.gameObject.name, component.GetType(), iterator.displayName, sceneName);
					}
				}
			}
		}
		// cause we'll do recursive call here, might consume lot of RAM, better clear all references possible here
		componentList.Clear();
		componentList = null;

		// then recursive to it childs
		if (target.transform.childCount > 0) {
			for (int n = 0, count = target.transform.childCount; n < count; n++) {
				var child = target.transform.GetChild(n).gameObject;
				FindReferenceInGameObjectAndItsChilds(child, suspect);
			}
		}
	}

	static void FindReferenceInArraySerializeProperty(SerializedProperty property, Object suspect, Component component) {
		var arrayName = property.displayName;
		var arraySize = property.arraySize;
		/// If the SerializeProperty is an Array with size n. Then the next element will the
		/// SerializeFIeld with name "Size" - which display in inspector just before the array expand.
		/// Then, their will be the n SerializeProperty corressponding to the array.
		/// So, call NextVisible here, to skip the "Size" property which we dont need to check
		property.NextVisible(true);

		/// Loop through n properties in the Array,
		/// Use NextVisible n times to do this
		for (int k = 0; k < arraySize; k++) {
			if (!property.NextVisible(true))
				break;
			if (CompareReferenceOfSerializeProperty(property, suspect)) {
				const string LOG_FORMAT = "object[{0}], component[{1}], arrayField [{2}], index {3}, scene [{4}]";
				var sceneName = component.gameObject.scene.name;
				Debug.LogFormat(component, LOG_FORMAT, component.gameObject.name, component.GetType(), arrayName, k, sceneName);
			}
		}
	}

	static bool CompareReferenceOfSerializeProperty(SerializedProperty property, Object suspect) {
		if (property.propertyType == SerializedPropertyType.ObjectReference) {
			/// there're 2 sub-type of Object that we need to care :
			/// GameObject (extend from Object, not Component) + ScriptableObject (extend from Object)
			/// Component : mesh, collider, rigidbody .v.v..
			if ((property.objectReferenceValue is GameObject || property.objectReferenceValue is ScriptableObject)
				&& property.objectReferenceValue == suspect) {
				return true;
			}
			else if (property.objectReferenceValue is Component) {
				var castToComponent = property.objectReferenceValue as Component;
				if (castToComponent == null)
					return false;
				// same parent object? then this's our suspect
				var parentGameObject = castToComponent.gameObject;
				return parentGameObject == suspect;
			}
			// none of above types (other classes inherit from Object such as Sprite, Material ..)
			return property.objectReferenceValue == suspect;
		}
		// default is false
		return false;
	}
}