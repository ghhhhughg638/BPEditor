public static class TreeViewHelper
{
    /// <summary>
    /// 根据路径字符串数组构建 TreeView 的根级节点，并自动排序
    /// </summary>
    /// <param name="paths">路径数组，如 "A/B/C", "A/B/D"</param>
    /// <param name="treeView">目标 TreeView</param>
    public static void BuildTreeFromPaths(string[] paths, TreeView treeView)
    {
        if (treeView == null) throw new ArgumentNullException(nameof(treeView));
        treeView.BeginUpdate();
        treeView.Nodes.Clear();
        foreach (string path in paths)
            AddPathToNodeCollection(treeView.Nodes, path);
        SortTree(treeView);      // 自动排序
        treeView.EndUpdate();
    }

    /// <summary>
    /// 根据路径字符串数组构建指定父节点下的子树，并自动排序
    /// </summary>
    /// <param name="paths">路径数组，如 "X/Y", "X/Z"</param>
    /// <param name="parentNode">目标父节点（该节点本身不会作为路径的一部分）</param>
    public static void BuildTreeFromPaths(string[] paths, TreeNode parentNode)
    {
        if (parentNode == null) throw new ArgumentNullException(nameof(parentNode));
        var treeView = parentNode.TreeView;
        treeView?.BeginUpdate();
        foreach (string path in paths)
            AddPathToNodeCollection(parentNode.Nodes, path);
        SortTree(parentNode);     // 自动排序
        treeView?.EndUpdate();
    }

    // 核心方法：将单条路径添加到指定的 TreeNodeCollection 中
    public static void AddPathToNodeCollection(TreeNodeCollection nodes, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        string[] parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        TreeNodeCollection currentCollection = nodes;
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i].Trim();
            if (string.IsNullOrEmpty(part)) continue;

            TreeNode? existingNode = null;
            foreach (TreeNode node in currentCollection)
            {
                if (node.Text == part)
                {
                    existingNode = node;
                    break;
                }
            }

            if (existingNode != null)
            {
                currentCollection = existingNode.Nodes;
            }
            else
            {
                TreeNode newNode = new TreeNode(part);
                currentCollection.Add(newNode);
                currentCollection = newNode.Nodes;
            }
        }
    }

    // ------------------- 排序相关方法 -------------------

    /// <summary>
    /// 对 TreeView 所有节点递归排序（有子节点的在前，无子节点的在后，同组按文本字典序）
    /// </summary>
    public static void SortTree(TreeView treeView)
    {
        if (treeView == null) return;
        treeView.BeginUpdate();
        SortNodes(treeView.Nodes);
        treeView.EndUpdate();
    }

    /// <summary>
    /// 对指定节点及其所有子节点递归排序
    /// </summary>
    public static void SortTree(TreeNode parentNode)
    {
        if (parentNode == null) return;
        var treeView = parentNode.TreeView;
        treeView?.BeginUpdate();
        SortNodes(parentNode.Nodes);
        treeView?.EndUpdate();
    }

    // 递归排序核心：对当前层的子节点排序，并先处理深层节点
    private static void SortNodes(TreeNodeCollection nodes)
    {
        if (nodes == null || nodes.Count == 0) return;

        // 先递归排序所有子节点
        foreach (TreeNode node in nodes)
        {
            SortNodes(node.Nodes);
        }

        // 对当前层排序：有子节点的排在前面，无子节点的排在后面，各自内部按 Text 字典序
        var list = nodes.Cast<TreeNode>().ToList();
        list.Sort((a, b) =>
        {
            bool aHasChildren = a.Nodes.Count > 0;
            bool bHasChildren = b.Nodes.Count > 0;
            if (aHasChildren != bHasChildren)
                return bHasChildren.CompareTo(aHasChildren); // 有子节点 → 排更前
            return string.Compare(a.Text, b.Text, StringComparison.CurrentCulture);
        });

        nodes.Clear();
        nodes.AddRange(list.ToArray());
    }
}