using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using GroupedItems = System.Linq.IGrouping<string, System.Type>;


public class SelectorTypePopupWindow : PopupWindowContent
{
	#region Enumerators
	protected enum FilterType
	{
		NonGeneric = 1,
		Generic = 2,
		Abstract = 4,
		Interface = 8,
		Enum = 16
	}

	protected enum GroupBy
	{
		Name,
		Namespace,
		FirstSymbol
	}
	#endregion

	#region Properties
	protected bool AlphabeticalSorting { get; set; }
	protected string SearchText { get; set; }
	protected string SelectionHistory { get; set; }
	protected FilterType FilterMask { get; set; } = (FilterType)~0x00;
	protected GroupBy GroupMask { get; set; }
	protected Vector2 WindowSize { get; set; }
	protected Vector2 ScrollPosition { get; set; }
	protected IEnumerable<Type> DerivedTypes { get; set; }
	protected IEnumerable<Type> FilteredTypes { get; set; }
	protected GroupedItems[] DisplayItems { get; set; }
	protected Stack<KeyValuePair<GroupBy, GroupedItems[]>> SelectionStack { get; set; }
	protected Action<Type> OnSelectCallback { get; set; }
	protected float LineHeight => EditorGUIUtility.singleLineHeight;

	protected GUIStyle fieldStyle = default;
	protected GUIStyle FieldStyle
	{
		get
		{
			if (fieldStyle == null)
			{
				fieldStyle = new GUIStyle(EditorStyles.toolbarButton);
				fieldStyle.alignment = TextAnchor.MiddleLeft;
				fieldStyle.wordWrap = false;
			}

			return fieldStyle;
		}	
	}
	#endregion

	#region Constructors
	public SelectorTypePopupWindow (Rect rect, Func<Type, bool> searchTypePredicate, Action<Type> onSelectCallback)
	{
		this.OnSelectCallback = onSelectCallback;
		this.WindowSize = new Vector2(rect.width, LineHeight * 15);
		this.SelectionStack = new Stack<KeyValuePair<GroupBy, GroupedItems[]>>();
		this.FilteredTypes = DerivedTypes = GetDerivedTypes(searchTypePredicate);
		this.AlphabeticalSorting = FilteredTypes.Count() > 100;
		this.GroupMask = AlphabeticalSorting ? GroupBy.FirstSymbol : GroupBy.Name;
		this.UpdateDisplayItems();
	}
	#endregion

	#region Implementations
	public override Vector2 GetWindowSize () => WindowSize;

	public override void OnGUI (Rect rect)
	{
		rect.y += 5.0f;
		DrawSearchField(rect);

		rect.y += LineHeight;
		DrawFilterMask(rect);
		DrawSortingToggle(rect);

		rect.y += LineHeight + 3.0f;
		DrawBackButton(rect);

		rect.y += LineHeight;
		DrawContent(rect);
	}
	#endregion

	#region Methods
	protected void DrawSearchField (Rect rect)
    {
		Rect rectSearch = new Rect(rect.x + 5.0f, rect.y, rect.width - 10.0f, LineHeight);
		string searchText = EditorGUI.TextField(rectSearch, SearchText, EditorStyles.toolbarSearchField);

		if (searchText == SearchText)
			return;

		SearchText = searchText;
		SelectionStack.Clear();

		if (string.IsNullOrEmpty(searchText))
		{
			SelectionHistory = "Types";
			UpdateDisplayItems();
		}
		else
		{
			SelectionHistory = "Search";
			DisplayNextItems(DerivedTypes.Where(t => t.Name.IndexOf(SearchText,
				StringComparison.InvariantCultureIgnoreCase) != -1), GroupBy.Name);
		}
	}

	protected void DrawFilterMask(Rect rect)
	{
		GUIContent content = new GUIContent("Filter: ");
		EditorStyles.label.CalcMinMaxWidth(content, out float minWidth, out float maxWidth);
		Rect rectLabel = new Rect(rect.x + 5.0f, rect.y, minWidth, LineHeight);
		Rect rectMask = new Rect(rectLabel.xMax, rect.y, rect.width * 0.5f - minWidth, LineHeight);
		EditorGUI.LabelField(rectLabel, content);
		FilterType filterMask = (FilterType)EditorGUI.EnumFlagsField(rectMask, FilterMask, EditorStyles.miniPullDown);

		if (filterMask.Equals(FilterMask))
			return;

		if (filterMask < 0)
			filterMask = (FilterType)~0x00;

		Func<Type, bool> predicate = t => false;
		if (filterMask.HasFlag(FilterType.NonGeneric)) predicate += t => !t.IsGenericType;
		if (filterMask.HasFlag(FilterType.Generic)) predicate += t => t.IsGenericType;
		if (filterMask.HasFlag(FilterType.Abstract)) predicate += t => t.IsAbstract;
		if (filterMask.HasFlag(FilterType.Interface)) predicate += t => t.IsInterface;
		if (filterMask.HasFlag(FilterType.Enum)) predicate += t => t.IsEnum;

		FilterMask = filterMask;
		FilteredTypes = DerivedTypes
			.Where(t => predicate.GetInvocationList()
			.Any(d => ((Func<Type, bool>)d).Invoke(t)));

		UpdateDisplayItems();
	}

	protected void DrawSortingToggle (Rect rect)
	{
		GUIContent content = new GUIContent("Alpha sorting: ");
		EditorStyles.label.CalcMinMaxWidth(content, out float minWidth, out float maxWidth);
        Rect rectLabel = new Rect(rect.xMax - minWidth - LineHeight - 5.0f, rect.y, minWidth, LineHeight);
		Rect rectToggle = new Rect(rectLabel.xMax, rect.y, LineHeight, LineHeight);
		EditorGUI.LabelField(rectLabel, content);
        bool value = EditorGUI.Toggle(rectToggle, AlphabeticalSorting, EditorStyles.radioButton);

		if (value.Equals(AlphabeticalSorting))
			return;

		AlphabeticalSorting = value;
		GroupMask = value ? GroupBy.FirstSymbol : GroupBy.Name;
		UpdateDisplayItems();
	}

	protected void DrawContent (Rect rect)
    {
		Vector2 tempIconSize = EditorGUIUtility.GetIconSize();
		Rect rectBox = new Rect(rect.x, rect.y, rect.width, LineHeight * 12 - 8.0f);
		EditorGUI.DrawRect(rectBox, Color.black);
		Rect rectView = new Rect(rectBox.x, rectBox.y, rectBox.width - LineHeight * 0.5f, LineHeight * DisplayItems.Length);
		ScrollPosition = GUI.BeginScrollView(rectBox, ScrollPosition, rectView, false, true, GUIStyle.none, GUI.skin.verticalScrollbar);

		int viewCount = 15;
		int firstIndex = (int)(ScrollPosition.y / LineHeight);
		Rect rectIcon = new Rect(rectView.x, firstIndex * LineHeight + rect.y - LineHeight, LineHeight, LineHeight);
		Rect rectContent = new Rect(rectIcon.xMax, rectIcon.y, rectView.width - rectIcon.width, rectIcon.height);

		for (int i = firstIndex; i < Mathf.Min(DisplayItems.Length, firstIndex + viewCount); i++)
		{
			bool isGroup = SelectionStack.Count == 0 || SelectionStack.Peek().Key != GroupBy.Name;
			bool isNamespace = SelectionStack.Count > 0 && SelectionStack.Peek().Key == GroupBy.Namespace;
			Texture contentIcon = EditorGUIUtility.IconContent(isNamespace ? "d_FilterByType@2x" :
				isGroup ? "d_VerticalLayoutGroup Icon" : "d_pick@2x").image;

			rectIcon.y += LineHeight;
			rectContent.y = rectIcon.y;

			EditorGUIUtility.SetIconSize(Vector2.one * 10.0f);
			GUI.Label(rectIcon, contentIcon, FieldStyle);

			if (GUI.Button(rectContent, new GUIContent($" {DisplayItems[i].Key}"), FieldStyle))
			{
				OnSelectButton(DisplayItems[i]);
				break;
			}

			if (DisplayItems[i].Count() > 1)
			{
				Rect rectArrowIcon = new Rect(rectContent.xMax - rectIcon.width, rectIcon.y, rectIcon.width, rectIcon.height);
				GUI.Label(rectArrowIcon, EditorGUIUtility.IconContent("d_tab_next@2x").image, FieldStyle);
			}

			EditorGUIUtility.SetIconSize(tempIconSize);
		}

		GUI.EndScrollView();
	}

	protected void DrawBackButton(Rect rect)
	{
		GUI.enabled = SelectionHistory.IndexOf('/') != -1;
		Rect rectBackBtn = new Rect(rect.x, rect.y, rect.width, LineHeight);

		if (GUI.Button(rectBackBtn, new GUIContent(SelectionHistory), EditorStyles.miniButtonMid))
			OnBackButton();

		if (GUI.enabled)
		{
			Vector2 tempIconSize = EditorGUIUtility.GetIconSize();
			EditorGUIUtility.SetIconSize(Vector2.one * 10.0f);
			GUI.Label(rectBackBtn, EditorGUIUtility.IconContent("d_back@2x").image);
			EditorGUIUtility.SetIconSize(tempIconSize);
		}

		GUI.enabled = true;
	}
	
	protected void DisplayNextItems (IEnumerable<Type> types, GroupBy groupingType)
	{
		if (DisplayItems != null)
			SelectionStack.Push(new KeyValuePair<GroupBy, GroupedItems[]>(groupingType, DisplayItems));

		DisplayItems = GroupTypesBy(types, groupingType);
	}

	protected void UpdateDisplayItems ()
	{
		DisplayItems = GroupTypesBy(FilteredTypes, GroupMask);
		SelectionHistory = "Types";
		SelectionStack.Clear();
	}

	protected void OnBackButton ()
	{
		if (SelectionStack.Count > 0)
		{
			int lastIndexSeparator = SelectionHistory.LastIndexOf('/');
			SelectionHistory = SelectionHistory.Remove(lastIndexSeparator, 
				SelectionHistory.Length - lastIndexSeparator);

			DisplayItems = SelectionStack.Pop().Value;
		}
	}

	protected void OnSelectButton (GroupedItems items)
	{
		GroupBy groupingType = SelectionStack.Count > 0 ? 
			SelectionStack.Peek().Key : GroupMask;

		switch (groupingType)
		{
			case GroupBy.FirstSymbol:
				DisplayNextItems(items, GroupBy.Name);
				break;
			case GroupBy.Name:
				if (items.Count() == 1) SendResponse(items.First());
				else DisplayNextItems(items, GroupBy.Namespace);
				break;
			case GroupBy.Namespace:
				SendResponse(items.First());
				break;
			default: break;
		}

		SelectionHistory += $"/{items.Key}";
	}

	protected void SendResponse (Type resultType)
	{
		OnSelectCallback?.Invoke(resultType);
		editorWindow.Close();
	}

	protected GroupedItems[] GroupTypesBy (IEnumerable<Type> types, GroupBy groupingType)
	{
		Func<Type, string> predicate = default;
		switch (groupingType)
		{
			case GroupBy.Name: predicate = t => t.Name; break;
			case GroupBy.Namespace: predicate = t => t.Namespace; break;
			case GroupBy.FirstSymbol: predicate = t => t.Name.ToUpper()[0].ToString(); break;
			default: predicate = t => string.Empty; break;
		}

		return types.GroupBy(t => predicate(t)).OrderBy(t => t.Key).ToArray();
	}

	protected IEnumerable<Type> GetDerivedTypes(Func<Type, bool> predicate)
	{
		return AppDomain.CurrentDomain.GetAssemblies()
			.SelectMany(a => a.GetTypes())
			.Where(t => predicate.GetInvocationList()
			.All(d => ((Func<Type, bool>)d).Invoke(t)));
	}
	#endregion
}