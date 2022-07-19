using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using System.Reflection;


[CustomPropertyDrawer(typeof(TypeFieldAttribute))]
public class TypeFieldPropertyDrawer : PropertyDrawer
{
	#region Constants
	protected readonly Dictionary<string, TypeInfo> TypeInfoDictionary = new Dictionary<string, TypeInfo>();
	#endregion

    #region Properties
	protected TypeFieldAttribute Attribute => attribute as TypeFieldAttribute;
	protected float LineHeight => EditorGUIUtility.singleLineHeight;
	protected Rect TotalRect { get; set; }
	#endregion

	#region Implementations
	public override float GetPropertyHeight (SerializedProperty property, GUIContent label)
    {
		TypeInfo selectedInfo = default;
		if (!TypeInfoDictionary.TryGetValue(property.propertyPath, out selectedInfo))
			TypeInfoDictionary.Add(property.propertyPath, selectedInfo = new TypeInfo());

		return selectedInfo.CalcHeight();
    }

    public override void OnGUI (Rect position, SerializedProperty property, GUIContent label)
	{
		TotalRect = new Rect(position);
		label = EditorGUI.BeginProperty(position, label, property);

		TypeInfo selectedInfo = default;
		if (!TypeInfoDictionary.TryGetValue(property.propertyPath, out selectedInfo))
            TypeInfoDictionary.Add(property.propertyPath, selectedInfo = new TypeInfo());

		if (selectedInfo.SearchPredicate == default)
			selectedInfo.SearchPredicate = type => Attribute.baseType.IsAssignableFrom(type);
		
		if (!Attribute.hideLabel)
			position = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);

		if (selectedInfo.IsMissing)
			selectedInfo.MainType = Type.GetType(property.stringValue) ?? Type.Missing.GetType();
        else if (!selectedInfo.IsValid)
        {
            selectedInfo.GenericArgs = default;
            selectedInfo.MainType = Type.Missing.GetType();
        }

        Rect dropdownRect = new Rect(position) { height = LineHeight };
		DrawType(dropdownRect, selectedInfo);

		if (!selectedInfo.MainType.IsEquivalentTo(Type.GetType(property.stringValue)))
		{
			property.stringValue = selectedInfo.MainType.AssemblyQualifiedName;
			property.serializedObject.ApplyModifiedProperties();
		}

        EditorGUI.EndProperty();
	}
	#endregion

	#region Method
	protected void DrawType (Rect position, TypeInfo typeInfo)
	{
		if (typeInfo.IsGeneric)
			DrawGenericType(position, typeInfo);
		else
			DrawNonGenericType(position, typeInfo);
	}

	protected void DrawNonGenericType (Rect position, TypeInfo typeInfo)
	{
		typeInfo.GenericArgs = default;
		DrawTypeField(position, typeInfo);
	}

	protected void DrawGenericType (Rect position, TypeInfo typeInfo)
	{
		GUI.backgroundColor = typeInfo.MainType.IsGenericTypeDefinition ? Color.yellow : Color.white;
		Rect dropdownRect = new Rect(position);
		dropdownRect.width -= 40.0f;
		DrawTypeField(dropdownRect, typeInfo);
		GUI.backgroundColor = Color.white;

		Rect editToggleRect = new Rect(position) { xMin = dropdownRect.xMax };
		typeInfo.IsExpanded = GUI.Toggle(editToggleRect, typeInfo.IsExpanded, new GUIContent("Edit"), EditorStyles.miniButton);

		if (typeInfo.IsExpanded)
		{
            if (typeInfo.GenericArgs == default)
                typeInfo.DefineGenericArgs();

			position.y += LineHeight;
			for (int i = 0; i < typeInfo.GenericArgs.Length; i++)
			{
				GUI.backgroundColor = !typeInfo[i].IsMissing && !typeInfo[i].MainType.IsGenericTypeDefinition && 
					typeInfo[i].IsValid ? Color.green : Color.red;
				EditorStyles.helpBox.CalcMinMaxWidth(typeInfo[i].Content, out float minWidth, out float maxWidth);
				Rect argRect = new Rect(position.x, position.y, minWidth, LineHeight);
				EditorGUI.LabelField(argRect, typeInfo[i].Content, EditorStyles.helpBox);
				GUI.backgroundColor = Color.white;

				argRect.xMin = argRect.xMax;
				argRect.width = position.xMax - argRect.xMax;
                DrawType(argRect, typeInfo[i]);
				position.y += typeInfo[i].CalcHeight();
            }
		}
	}

	protected void DrawTypeField (Rect position, TypeInfo typeInfo)
	{
		if (EditorGUI.DropdownButton(position, new GUIContent(typeInfo.MainType.Name, typeInfo.MainType.ToString()), FocusType.Passive))
		{
			Rect rectPopup = new Rect(TotalRect) { y = position.y, height = LineHeight };
			PopupWindow.Show(rectPopup, new SelectorTypePopupWindow(rectPopup, typeInfo.SearchPredicate, typeInfo.ApplyType));
		}
	}
    #endregion

    #region Nested Types
	protected class TypeInfo
    {
        #region Fields
        public bool IsExpanded = default;
		public Type MainType = Type.Missing.GetType();
		public TypeInfo Parent = default;
		public TypeInfo[] GenericArgs = default;
		public Func<Type, bool> SearchPredicate = default;
		public GUIContent Content = GUIContent.none;
		#endregion

		#region Properties
		public bool IsGeneric => MainType.IsGenericType;
		public bool IsMissing => MainType.IsEquivalentTo(Type.Missing.GetType());
		public bool IsValid => SearchPredicate.GetInvocationList()
			.All(d => ((Func<Type, bool>) d).Invoke(MainType));
		#endregion

		#region Indexers
		public TypeInfo this[int index] => GenericArgs[index];
		#endregion

		#region Methods
		public void ApplyType (Type type = default)
        {
			if (type != default)
			{
				MainType = type;
				GenericArgs = default;
			}

			if (Parent != null)
			{
				Parent.MakeGenericType();
				Parent.ApplyType();
			}
        }

		public void MakeGenericType()
		{
			if (GenericArgs.All(t => !t.IsMissing && !t.MainType.IsGenericTypeDefinition))
			{
				Type[] args = GenericArgs.Select(p => p.MainType).ToArray();
				MainType = MainType.GetGenericTypeDefinition().MakeGenericType(args);
			}
			else
				MainType = MainType.GetGenericTypeDefinition();
		}

		public float CalcHeight ()
        {
			if (!IsExpanded || GenericArgs == default)
				return EditorGUIUtility.singleLineHeight;

			return GenericArgs.Sum(p => p.CalcHeight()) + EditorGUIUtility.singleLineHeight;
		}

        public void DefineGenericArgs ()
        {
			Type typeDef = MainType.GetGenericTypeDefinition();
			Type[] defArgTypes = typeDef.GetGenericArguments();
			Type[] argTypes = MainType.GenericTypeArguments;

			GenericArgs = new TypeInfo[defArgTypes.Length];
			for (int i = 0; i < GenericArgs.Length; i++)
			{
				GenericArgs[i] = new TypeInfo { Parent = this };
				GenericArgs[i].DefineGenericParams(defArgTypes[i]);

				if (!MainType.IsGenericTypeDefinition)
					GenericArgs[i].MainType = argTypes[i];
			}
        }

        public void DefineGenericParams (Type argType)
		{
			Content = new GUIContent(argType.Name);
			SearchPredicate = type => typeof(System.Object).IsAssignableFrom(type);
			if (!argType.IsGenericParameter)
				return;

			List<string> tooltipList = new List<string>();
			if (argType.ContainsGenericParameters)
			{
				Type[] paramTypes = argType.GetGenericParameterConstraints();
				SearchPredicate = type => paramTypes.All(p => (p.IsGenericType ? p.GetGenericTypeDefinition() : p).IsAssignableFrom(type));
				tooltipList.AddRange(paramTypes.Select(p => p.Name));
			}

			GenericParameterAttributes constraints = argType.GenericParameterAttributes & 
				GenericParameterAttributes.SpecialConstraintMask;
			if (constraints == GenericParameterAttributes.None)
			{
                //tooltipList.Add("object");
            }
			else
			{
				if ((constraints & GenericParameterAttributes.ReferenceTypeConstraint) != 0)
				{
					SearchPredicate += type => type.IsClass;
					tooltipList.Add("class");
				}

				if ((constraints & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0)
				{
					SearchPredicate += type => type.IsValueType;
					tooltipList.Add("struct");
				}

				if ((constraints & GenericParameterAttributes.DefaultConstructorConstraint) != 0)
				{
					SearchPredicate += type => !type.IsConstructedGenericType;
					tooltipList.Add("new()");
				}
			}

			Content.tooltip = string.Join(", ", tooltipList);
		}
		#endregion
	}
    #endregion
}