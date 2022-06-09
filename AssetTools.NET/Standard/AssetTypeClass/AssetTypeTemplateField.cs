﻿using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace AssetsTools.NET
{
    public class AssetTypeTemplateField
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public AssetValueType ValueType { get; set; }
        public bool IsArray { get; set; }
        public bool IsAligned { get; set; }
        public bool HasValue { get; set; }
        public List<AssetTypeTemplateField> Children { get; set; }

        public void FromTypeTree(TypeTreeType typeTreeType)
        {
            int fieldIndex = 0;
            FromTypeTree(typeTreeType, ref fieldIndex);
        }

        private void FromTypeTree(TypeTreeType typeTreeType, ref int fieldIndex)
        {
            TypeTreeNode field = typeTreeType.Nodes[fieldIndex];
            Name = field.GetNameString(typeTreeType.StringBuffer);
            Type = field.GetTypeString(typeTreeType.StringBuffer);
            ValueType = AssetTypeValueField.GetValueTypeByTypeName(Type);
            IsArray = field.TypeFlags == 1;
            IsAligned = (field.MetaFlags & 0x4000) != 0;
            HasValue = ValueType != AssetValueType.None;

            Children = new List<AssetTypeTemplateField>();

            for (fieldIndex++; fieldIndex < typeTreeType.Nodes.Count; fieldIndex++)
            {
                TypeTreeNode typeTreeField = typeTreeType.Nodes[fieldIndex];
                if (typeTreeField.Level <= field.Level)
                {
                    fieldIndex--;
                    break;
                }

                AssetTypeTemplateField assetField = new AssetTypeTemplateField();
                assetField.FromTypeTree(typeTreeType, ref fieldIndex);
                Children.Add(assetField);
            }
            Children.TrimExcess();
        }

        public void FromClassDatabase(ClassDatabaseFile cldbFile, ClassDatabaseType cldbType, bool preferEditor = false)
        {
            if (cldbType.EditorRootNode == null && cldbType.ReleaseRootNode == null)
                throw new Exception("No root nodes were found!");

            ClassDatabaseTypeNode node = cldbType.GetPreferredNode(preferEditor);

            FromClassDatabase(cldbFile.StringTable, node);
        }

        private void FromClassDatabase(ClassDatabaseStringTable strTable, ClassDatabaseTypeNode node)
        {
            Name = strTable.GetString(node.FieldName);
            Type = strTable.GetString(node.TypeName);
            ValueType = AssetTypeValueField.GetValueTypeByTypeName(Type);
            IsArray = node.TypeFlags == 1;
            IsAligned = (node.MetaFlag & 0x4000) != 0;
            HasValue = ValueType != AssetValueType.None;

            Children = new List<AssetTypeTemplateField>(node.Children.Count);
            foreach (ClassDatabaseTypeNode childNode in node.Children)
            {
                AssetTypeTemplateField childField = new AssetTypeTemplateField();
                childField.FromClassDatabase(strTable, childNode);
                Children.Add(childField);
            }
        }

        public AssetTypeValueField MakeValue(AssetsFileReader reader)
        {
            AssetTypeValueField valueField = new AssetTypeValueField
            {
                TemplateField = this
            };
            valueField = ReadType(reader, valueField);
            return valueField;
        }

        public AssetTypeValueField MakeValue(AssetsFileReader reader, long position)
        {
            reader.Position = position;
            return MakeValue(reader);
        }

        public AssetTypeValueField ReadType(AssetsFileReader reader, AssetTypeValueField valueField)
        {
            if (valueField.TemplateField.IsArray)
            {
                int arrayChildCount = valueField.TemplateField.Children.Count;
                if (arrayChildCount == 2)
                {
                    AssetValueType sizeType = valueField.TemplateField.Children[0].ValueType;
                    if (sizeType == AssetValueType.Int32 || sizeType == AssetValueType.UInt32)
                    {
                        if (valueField.TemplateField.ValueType == AssetValueType.ByteArray)
                        {
                            valueField.Children = new List<AssetTypeValueField>(0);

                            int size = reader.ReadInt32();
                            byte[] data = reader.ReadBytes(size);

                            if (valueField.TemplateField.IsAligned)
                                reader.Align();

                            valueField.Value = new AssetTypeValue(AssetValueType.ByteArray, data);
                        }
                        else
                        {
                            int size = reader.ReadInt32();
                            valueField.Children = new List<AssetTypeValueField>(size);
                            for (int i = 0; i < size; i++)
                            {
                                AssetTypeValueField childField = new AssetTypeValueField();
                                childField.TemplateField = valueField.TemplateField.Children[1];
                                valueField.Children.Add(ReadType(reader, childField));
                            }
                            valueField.Children.TrimExcess();

                            if (valueField.TemplateField.IsAligned)
                                reader.Align();

                            AssetTypeArrayInfo ata = new AssetTypeArrayInfo
                            {
                                size = size
                            };

                            valueField.Value = new AssetTypeValue(AssetValueType.Array, ata);
                        }
                    }
                    else
                    {
                        throw new Exception($"Expected int array size type, found {sizeType} instead!");
                    }
                }
                else
                {
                    throw new Exception($"Expected array to have two children, found {arrayChildCount} instead!");
                }
            }
            else
            {
                AssetValueType type = valueField.TemplateField.ValueType;
                if (type == AssetValueType.None)
                {
                    int childCount = valueField.TemplateField.Children.Count;
                    valueField.Children = new List<AssetTypeValueField>(childCount);
                    for (int i = 0; i < childCount; i++)
                    {
                        AssetTypeValueField childField = new AssetTypeValueField();
                        childField.TemplateField = valueField.TemplateField.Children[i];
                        valueField.Children.Add(ReadType(reader, childField));
                    }
                    valueField.Children.TrimExcess();
                    valueField.Value = null;

                    if (valueField.TemplateField.IsAligned)
                        reader.Align();
                }
                else
                {
                    valueField.Value = new AssetTypeValue(type, null);
                    if (type == AssetValueType.String)
                    {
                        int length = reader.ReadInt32();
                        valueField.Value = new AssetTypeValue(reader.ReadBytes(length), true);
                        reader.Align();
                    }
                    else
                    {
                        int childCount = valueField.TemplateField.Children.Count;
                        if (childCount == 0)
                        {
                            valueField.Children = new List<AssetTypeValueField>(0);
                            switch (valueField.TemplateField.ValueType)
                            {
                                case AssetValueType.Int8:
                                    valueField.Value = new AssetTypeValue(reader.ReadSByte());
                                    break;
                                case AssetValueType.UInt8:
                                case AssetValueType.Bool:
                                    valueField.Value = new AssetTypeValue(reader.ReadByte());
                                    break;
                                case AssetValueType.Int16:
                                    valueField.Value = new AssetTypeValue(reader.ReadInt16());
                                    break;
                                case AssetValueType.UInt16:
                                    valueField.Value = new AssetTypeValue(reader.ReadUInt16());
                                    break;
                                case AssetValueType.Int32:
                                    valueField.Value = new AssetTypeValue(reader.ReadInt32());
                                    break;
                                case AssetValueType.UInt32:
                                    valueField.Value = new AssetTypeValue(reader.ReadUInt32());
                                    break;
                                case AssetValueType.Int64:
                                    valueField.Value = new AssetTypeValue(reader.ReadInt64());
                                    break;
                                case AssetValueType.UInt64:
                                    valueField.Value = new AssetTypeValue(reader.ReadUInt64());
                                    break;
                                case AssetValueType.Float:
                                    valueField.Value = new AssetTypeValue(reader.ReadSingle());
                                    break;
                                case AssetValueType.Double:
                                    valueField.Value = new AssetTypeValue(reader.ReadDouble());
                                    break;
                            }

                            if (valueField.TemplateField.IsAligned)
                                reader.Align();
                        }
                        else if (valueField.TemplateField.ValueType != AssetValueType.None)
                        {
                            throw new Exception("Cannot read value of field with children!");
                        }
                    }
                }

            }
            return valueField;
        }
    }
}
