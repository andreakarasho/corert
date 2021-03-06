// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using ILCompiler.DependencyAnalysisFramework;
using Internal.TypeSystem;

namespace ILCompiler.DependencyAnalysis
{
    public interface IHasStartSymbol
    {
        ObjectAndOffsetSymbolNode StartSymbol { get; }
    }

    /// <summary>
    /// Represents an array of <typeparamref name="TEmbedded"/> nodes. The contents of this node will be emitted
    /// by placing a starting symbol, followed by contents of <typeparamref name="TEmbedded"/> nodes (optionally
    /// sorted using provided comparer), followed by ending symbol.
    /// </summary>
    public class ArrayOfEmbeddedDataNode<TEmbedded> : EmbeddedDataContainerNode, IHasStartSymbol
        where TEmbedded : EmbeddedObjectNode
    {
        private HashSet<TEmbedded> _nestedNodes = new HashSet<TEmbedded>();
        private List<TEmbedded> _nestedNodesList = new List<TEmbedded>();
        private IComparer<TEmbedded> _sorter;

        public ArrayOfEmbeddedDataNode(string startSymbolMangledName, string endSymbolMangledName, IComparer<TEmbedded> nodeSorter) : base(startSymbolMangledName, endSymbolMangledName)
        {
            _sorter = nodeSorter;
        }
        
        public void AddEmbeddedObject(TEmbedded symbol)
        {
            lock (_nestedNodes)
            {
                if (_nestedNodes.Add(symbol))
                {
                    _nestedNodesList.Add(symbol);
                }
                symbol.ContainingNode = this;
            }
        }

        protected override string GetName(NodeFactory factory) => $"Region {StartSymbol.GetMangledName(factory.NameMangler)}";

        public override ObjectNodeSection Section => ObjectNodeSection.DataSection;
        public override bool IsShareable => false;

        public override bool StaticDependenciesAreComputed => true;

        protected IEnumerable<TEmbedded> NodesList =>  _nestedNodesList;

        protected virtual void GetElementDataForNodes(ref ObjectDataBuilder builder, NodeFactory factory, bool relocsOnly)
        {
            int index = 0;
            foreach (TEmbedded node in NodesList)
            {
                if (!relocsOnly)
                {
                    node.InitializeOffsetFromBeginningOfArray(builder.CountBytes);
                    node.InitializeIndexFromBeginningOfArray(index++);
                }

                node.EncodeData(ref builder, factory, relocsOnly);
                if (node is ISymbolDefinitionNode symbolDef)
                {
                    builder.AddSymbol(symbolDef);
                }
            }
        }

        public override ObjectData GetData(NodeFactory factory, bool relocsOnly)
        {
            ObjectDataBuilder builder = new ObjectDataBuilder(factory, relocsOnly);
            builder.RequireInitialPointerAlignment();

            if (_sorter != null)
                _nestedNodesList.Sort(_sorter);

            builder.AddSymbol(StartSymbol);

            GetElementDataForNodes(ref builder, factory, relocsOnly);

            EndSymbol.SetSymbolOffset(builder.CountBytes);
            builder.AddSymbol(EndSymbol);

            ObjectData objData = builder.ToObjectData();
            return objData;
        }

        public override bool ShouldSkipEmittingObjectNode(NodeFactory factory)
        {
            return _nestedNodesList.Count == 0;
        }

        protected override DependencyList ComputeNonRelocationBasedDependencies(NodeFactory factory)
        {
            DependencyList dependencies = new DependencyList();
            dependencies.Add(StartSymbol, "StartSymbol");
            dependencies.Add(EndSymbol, "EndSymbol");

            return dependencies;
        }

        protected internal override int Phase => (int)ObjectNodePhase.Ordered;

        public override int ClassCode => (int)ObjectNodeOrder.ArrayOfEmbeddedDataNode;
    }
}
