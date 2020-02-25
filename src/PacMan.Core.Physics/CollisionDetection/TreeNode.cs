﻿using System;

namespace PacMan.Core.DataStructures.Trees
{
    public class TreeNode
    {
        public TreeNode(AxisAlignedBoundingBox value)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public TreeNode(TreeNode left, TreeNode right)
        {
            Left = left ?? throw new ArgumentNullException(nameof(left));
            Right = right ?? throw new ArgumentNullException(nameof(right));
            Value = AxisAlignedBoundingBox.Combine(left.Value, right.Value);
        }

        public AxisAlignedBoundingBox Value { get; }

        public TreeNode Left { get; }

        public TreeNode Right { get; }
    }
}