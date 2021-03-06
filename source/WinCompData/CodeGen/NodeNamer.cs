// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

using Microsoft.Toolkit.Uwp.UI.Lottie.WinCompData.Tools;

namespace Microsoft.Toolkit.Uwp.UI.Lottie.WinCompData.CodeGen
{
    /// <summary>
    /// Generates names for the nodes in an <see cref="ObjectGraph{T}"/>.
    /// </summary>
    /// <typeparam name="TNode">The type of the data associated with each node.</typeparam>
#if PUBLIC_WinCompData
    public
#endif
    static class NodeNamer<TNode>
        where TNode : Graph.Node<TNode>, new()
    {
        /// <summary>
        /// Takes a list of nodes and generates unique names for them. Returns a list of node + name pairs.
        /// The names are chosen to be descriptive and usable in code generation.
        /// </summary>
        /// <returns>A lot of node + name pairs usable in code generation.</returns>
        public static IEnumerable<(TNode, string)> GenerateNodeNames(IEnumerable<TNode> nodes)
        {
            var nodesByTypeName = new Dictionary<string, List<TNode>>();
            foreach (var node in nodes)
            {
                string baseName;

                switch (node.Type)
                {
                    case Graph.NodeType.CompositionObject:
                        baseName = DescribeCompositionObject(node, (CompositionObject)node.Object);
                        break;
                    case Graph.NodeType.CompositionPath:
                        baseName = "CompositionPath";
                        break;
                    case Graph.NodeType.CanvasGeometry:
                        baseName = "Geometry";
                        break;
                    default:
                        throw new InvalidOperationException();
                }

                if (!nodesByTypeName.TryGetValue(baseName, out var nodeList))
                {
                    nodeList = new List<TNode>();
                    nodesByTypeName.Add(baseName, nodeList);
                }

                nodeList.Add(node);
            }

            // Set the names on each node.
            foreach (var entry in nodesByTypeName)
            {
                var baseName = entry.Key;
                var n = entry.Value;
                if (n.Count == 1)
                {
                    // There's only 1 of this type of node. No suffix needed.
                    yield return (n[0], baseName);
                }
                else
                {
                    // Multiple nodes of this type. Append a counter suffix.

                    // Use only as many digits as necessary to express the largest count.
                    var digitsRequired = (int)Math.Ceiling(Math.Log10(n.Count + 1));
                    var counterFormat = new string('0', digitsRequired);

                    for (var i = 0; i < n.Count; i++)
                    {
                        yield return (n[i], $"{baseName}_{i.ToString(counterFormat)}");
                    }
                }
            }
        }

        // Returns the value from the given keyframe, or null.
        static T? ValueFromKeyFrame<T>(KeyFrameAnimation<T>.KeyFrame kf)
            where T : struct
        {
            return kf is KeyFrameAnimation<T>.ValueKeyFrame valueKf ? (T?)valueKf.Value : null;
        }

        static (T? First, T? Last) FirstAndLastValuesFromKeyFrame<T>(KeyFrameAnimation<T> animation)
            where T : struct
        {
            // If there's only one keyframe, return it as the last value and leave the first value null.
            var first = animation.KeyFrameCount > 1 ? ValueFromKeyFrame(animation.KeyFrames.First()) : null;
            var last = ValueFromKeyFrame(animation.KeyFrames.Last());
            return (first, last);
        }

        // Returns a string for use in an identifier that describes a ColorKeyFrameAnimation, or null
        // if the animation cannot be described.
        static string DescribeAnimationRange(ColorKeyFrameAnimation animation)
        {
            (var firstValue, var lastValue) = FirstAndLastValuesFromKeyFrame(animation);
            return (firstValue.HasValue && lastValue.HasValue) ? $"{firstValue.Value.Name}_to_{lastValue.Value.Name}" : null;
        }

        static string DescribeAnimationRange(ScalarKeyFrameAnimation animation)
        {
            (var firstValue, var lastValue) = FirstAndLastValuesFromKeyFrame(animation);
            return lastValue.HasValue
                ? firstValue.HasValue
                    ? $"{FloatId(firstValue.Value)}_to_{FloatId(lastValue.Value)}"
                    : $"to_{FloatId(lastValue.Value)}"
                : null;
        }

        static string TryGetAnimatedPropertyName(TNode node)
        {
            // Find the property name that references this animation.
            var animators =
                (from inref in node.InReferences
                 let referrer = (CompositionObject)inref.Node.Object
                 from animator in referrer.Animators
                 where animator.Animation == node.Object
                 select animator.AnimatedProperty).Distinct().ToArray();

            return animators.Length == 1 ? animators[0] : null;
        }

        static string SanitizePropertyName(string propertyName)
        {
            return propertyName == null ? null : propertyName.Replace(".", string.Empty);
        }

        static string DescribeCompositionObject(TNode node, CompositionObject obj)
        {
            string result = null;
            switch (obj.Type)
            {
                case CompositionObjectType.ColorKeyFrameAnimation:
                    {
                        result = "ColorAnimation";
                        var description = DescribeAnimationRange((ColorKeyFrameAnimation)obj);
                        if (description != null)
                        {
                            result += $"_{description}";
                        }
                    }

                    break;
                case CompositionObjectType.ScalarKeyFrameAnimation:
                    {
                        var animatedPropertyName = SanitizePropertyName(TryGetAnimatedPropertyName(node));
                        result = $"{animatedPropertyName}ScalarAnimation";
                        var description = DescribeAnimationRange((ScalarKeyFrameAnimation)obj);
                        if (description != null)
                        {
                            result += $"_{description}";
                       }
                    }

                    break;
                case CompositionObjectType.Vector2KeyFrameAnimation:
                    {
                        var animatedPropertyName = SanitizePropertyName(TryGetAnimatedPropertyName(node));
                        result = $"{animatedPropertyName}Vector2Animation";
                    }

                    break;
                case CompositionObjectType.CompositionColorBrush:
                    // Color brushes that are not animated get names describing their color.
                    // Optimization ensures there will only be one brush for any one non-animated color.
                    var brush = (CompositionColorBrush)obj;
                    if (brush.Animators.Count > 0)
                    {
                        // Brush is animated. Give it a name based on the colors in the animation.
                        var colorAnimation = (ColorKeyFrameAnimation)brush.Animators.Where(a => a.Animation is ColorKeyFrameAnimation).First().Animation;
                        var description = DescribeAnimationRange(colorAnimation);
                        if (description != null)
                        {
                            result = $"AnimatedColorBrush_{description}";
                        }
                        else
                        {
                            result = "AnimatedColorBrush";
                        }
                    }
                    else
                    {
                        // Brush is not animated. Give it a name based on the color.
                        result = $"ColorBrush_{brush.Color.Name}";
                    }

                    break;
                case CompositionObjectType.CompositionRectangleGeometry:
                    var rectangle = (CompositionRectangleGeometry)obj;
                    result = $"Rectangle_{Vector2Id(rectangle.Size)}";
                    break;
                case CompositionObjectType.CompositionRoundedRectangleGeometry:
                    var roundedRectangle = (CompositionRoundedRectangleGeometry)obj;
                    result = $"RoundedRectangle_{Vector2Id(roundedRectangle.Size)}";
                    break;
                case CompositionObjectType.CompositionEllipseGeometry:
                    var ellipse = (CompositionEllipseGeometry)obj;
                    result = $"Ellipse_{Vector2Id(ellipse.Radius)}";
                    break;
                case CompositionObjectType.ExpressionAnimation:
                    var expressionAnimation = (ExpressionAnimation)obj;
                    var expression = expressionAnimation.Expression;
                    var expressionType = expression.InferredType;
                    if (expressionType.IsValid && !expressionType.IsGeneric)
                    {
                        result = $"{expressionType.Constraints.ToString()}ExpressionAnimation";
                    }
                    else
                    {
                        result = "ExpressionAnimation";
                    }

                    break;
                case CompositionObjectType.StepEasingFunction:
                    // Recognize 2 common patterns: HoldThenStep and StepThenHold
                    var stepEasingFunction = (StepEasingFunction)obj;
                    if (stepEasingFunction.StepCount == 1 && stepEasingFunction.IsFinalStepSingleFrame && !stepEasingFunction.IsInitialStepSingleFrame)
                    {
                        result = "HoldThenStepEasingFunction";
                    }
                    else if (stepEasingFunction.StepCount == 1 && stepEasingFunction.IsInitialStepSingleFrame && !stepEasingFunction.IsFinalStepSingleFrame)
                    {
                        result = "StepThenHoldEasingFunction";
                    }
                    else
                    {
                        // Didn't recognize the pattern.
                        goto default;
                    }

                    break;
                default:
                    result = obj.Type.ToString();
                    break;
            }

            // Remove the "Composition" prefix so the name is easier to read.
            const string compositionPrefix = "Composition";
            if (result.StartsWith(compositionPrefix))
            {
                result = result.Substring(compositionPrefix.Length);
            }

            return result;
        }

        static string FloatId(float value) => value.ToString("0.###", CultureInfo.InvariantCulture).Replace('.', 'p').Replace('-', 'm');

        // A Vector2 for use in an id.
        static string Vector2Id(Vector2 size)
        {
            return size.X == size.Y
                ? FloatId(size.X)
                : $"{FloatId(size.X)}x{FloatId(size.Y)}";
        }
    }
}
