using UnityEngine;
using System.Collections;
using UnityEditor;
using System.Collections.Generic;
using UnityEditor.SceneManagement;

/// <summary>
/// Main class (and also the main class of this tool :v).
/// Remember to put it in any Editor foldoer to let it work.
/// Follow the Readme or decription in Sample scene to know how to use this tool.
/// </summary>
public class ReferenceFilter : EditorWindow {

	/// <summary> temp, currently, this func too slow, dont use it,
	/// just keep an open code for reading later </summary>
	//	[MenuItem ("Assets/What objects use this?", false, 20)]
	private static void OnSearchForReferences() {
		//		string final = "";
		//		List<UnityEngine.Object> matches = new List<UnityEngine.Object> ();
		//		int iid = Selection.activeInstanceID;
		//		if (AssetDatabase.IsMainAsset (iid))
		//		{
		//			// only main assets have unique paths
		//			string path = AssetDatabase.GetAssetPath (iid);
		//			// strip down the name
		//			final = System.IO.Path.GetFileNameWithoutExtension (path);
		//		}
		//		else
		//		{
		//			Debug.Log ("Error Asset not found");
		//			return;
		//		}
		//		// get everything
		//		Object[] _Objects = Resources.FindObjectsOfTypeAll (typeof(Object));
		//		//loop through everything
		//		foreach (Object go in _Objects)
		//		{
		//			// needs to be an array
		//			Object[] g = new Object[1];
		//			g [0] = go;
		//			// All objects
		//			Object[] depndencies = EditorUtility.CollectDependencies (g);
		//			foreach (Object o in depndencies)
		//				if (string.Compare (o.name.ToString (), final) == 0)
		//					matches.Add (go);// add it to our list to highlight
		//		}
		//		Selection.objects = matches.ToArray ();
		//		matches.Clear (); // clear the list 
	}


	[MenuItem("CONTEXT/Transform/Search reference in scene")]
	private static void SearchReferenceInScene(MenuCommand command) {
		ClearConsole();
		var parentGameObject = ((Component)command.context).gameObject;
		Debug.LogFormat("----- Find GameObject reference : {0}", parentGameObject.name);
		FindObjectReferenceInCurrentScene(parentGameObject);
	}


	[MenuItem("FindReference/Search Object References in active scene")]
	private static void ObjectSearchReferenceInScene() {
		// scriptableObject case
		if(Selection.activeObject is ScriptableObject) {
			var castToSO = Selection.activeObject as ScriptableObject;
			ClearConsole();
			Debug.LogFormat("----- Find ScriptableObject reference : {0}", castToSO.name);
			FindObjectReferenceInCurrentScene(castToSO);
			return;
		}
		// GameObject case (on scene or prefab)
		var selectedObject = Selection.activeGameObject;
		if(selectedObject == null) {
			Debug.LogWarning("You must select an object !!");
			return;
		}
		ClearConsole();
		Debug.LogFormat("----- Find GameObject reference : {0}", selectedObject.name);
		FindObjectReferenceInCurrentScene(selectedObject);
	}


	[MenuItem("Tools/Clear Console")]// %#c")] // CMD + SHIFT + C
	static void ClearConsole() {
		// This simply does "LogEntries.Clear()" the long way:
		const string LOG_ENTRIES_PATH = "UnityEditorInternal.LogEntries,UnityEditor.dll";
		var logEntries = System.Type.GetType(LOG_ENTRIES_PATH);
		if(logEntries == null) {
			Debug.LogWarning("ReferenceFinder, cannot clear console windows");
			return;
		}
		var clearMethod = logEntries.GetMethod("Clear", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
		if(clearMethod == null) {
			Debug.LogWarning("ReferenceFinder, cannot find [Clear] method in dll to invoke, console not clear");
			return;
		}
		clearMethod.Invoke(null, null);
	}


	static void FindObjectReferenceInCurrentScene(Object suspect) {
		var currentScene = EditorSceneManager.GetActiveScene();
		var sceneRootList = new List<GameObject>(currentScene.rootCount);
		currentScene.GetRootGameObjects(sceneRootList);

		/// Use a loop throught rootObjects.GetComponentsInChildRen(true) in the active scene instead of :
		/// Object.FindObjectsOfType : only search in ACTIVE objects in scene
		/// Resources.FindObjectsOfTypeAll : too slow, cause it find in everything in our project
		for(int m = 0, amount = sceneRootList.Count; m < amount; m++) {
			// remember to find in Deactive object too (GetCompone...(true))
			var componentList = sceneRootList[m].GetComponentsInChildren<Component>(true);
			for(int n = 0; n < componentList.Length; n++) {
				var component = componentList[n];
				if(component == null)
					continue;// remember this checking null step

				/// use SerializeObject, so we can iterate through all serialized field in the component
				var serializeObject = new SerializedObject(component);
				var iterator = serializeObject.GetIterator();

				while(iterator.NextVisible(true)) {
					/// Here we'll checking 2 cases:
					/// Array : will iterator through that array
					/// Normal property : no iterator, just compare.
					/// In those cases, we have difference Log command
					/// NOTES : if the SerializeProperty.type == "string", then it's isArray also = true
					/// we should ignore this case, it cause "operation is not possible when moved past all properties" exception
					const string EXCLUDE_STRING_TYPE = "string";
					if(iterator.isArray && iterator.type != EXCLUDE_STRING_TYPE) {
						FindReferenceInArraySerializeProperty(iterator, suspect, component);
					}
					else {
						const string FORMAT = "object[{0}], component[{1}], field [{2}]";
						if(CompareReferenceOfSerializeProperty(iterator, suspect, component)) {
							Debug.LogFormat(component, FORMAT, component.gameObject.name, component.GetType(), iterator.displayName);
						}
					}
				}
			}
		}
		Debug.Log("------ Search Finished ---------");
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
		for(int k = 0; k < arraySize; k++) {
			if(!property.NextVisible(true))
				break;
			if(CompareReferenceOfSerializeProperty(property, suspect, component)) {
				const string LOG_FORMAT = "object[{0}], component[{1}], arrayField [{2}], index {3}";
				Debug.LogFormat(component, LOG_FORMAT, component.gameObject.name, component.GetType(), arrayName, k);
			}
		}
	}

	static bool CompareReferenceOfSerializeProperty(SerializedProperty property, Object suspect, Component component) {
		if(property.propertyType == SerializedPropertyType.ObjectReference) {
			/// currently, there're 2 sub-type of Object that we need to care :
			/// 1. GameObject (extend from Object, not Component) + ScriptableObject (also extend from Object)
			/// 2. Component : mesh, collider, rigidbody .v.v..
			if((property.objectReferenceValue is GameObject || property.objectReferenceValue is ScriptableObject)
				&& property.objectReferenceValue == suspect) {
				return true;
			}
			else if(property.objectReferenceValue is Component) {
				var castToComponent = property.objectReferenceValue as Component;
				if(castToComponent == null)
					return false;

				var parentGameObject = castToComponent.gameObject;
				return parentGameObject == suspect;
			}
		}
		// default is false
		return false;
	}
}