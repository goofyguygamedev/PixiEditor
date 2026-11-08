using PixiEditor.ChangeableDocument.Changeables.Graph.Nodes.Shapes.Data;
using Drawie.Backend.Core.ColorsImpl.Paintables;
using Drawie.Backend.Core.Numerics;
using Drawie.Numerics;

namespace PixiEditor.Models.Serialization.Factories;

internal class LineSerializationFactory : VectorShapeSerializationFactory<LineVectorData> 
{
    public override string DeserializationId { get; } = "PixiEditor.LineData";

    protected override void AddSpecificData(ByteBuilder builder, LineVectorData original)
    {
        builder.AddVecD(original.Start);
        builder.AddVecD(original.End);

        // Appended after the original two points so files written before curves existed still
        // deserialize: they just run out of bytes before this.
        builder.AddVecDList(original.ControlPoints);
        builder.AddDouble(original.Smoothness);
    }

    protected override bool DeserializeVectorData(ByteExtractor extractor, Matrix3X3 matrix, Paintable strokePaintable,
        bool fill, Paintable fillPaintable,
        float strokeWidth, (string serializerName, string serializerVersion) serializerData,
        out LineVectorData original)
    {
        VecD start = extractor.GetVecD();
        VecD end = extractor.GetVecD();

        List<VecD> controlPoints = new();
        double smoothness = 1.0;

        if (extractor.HasMoreData)
        {
            controlPoints = extractor.GetVecDList();
            smoothness = extractor.GetDouble();
        }

        original = new LineVectorData(start, end)
        {
            Stroke = strokePaintable,
            FillPaintable = fillPaintable,
            StrokeWidth = strokeWidth,
            TransformationMatrix = matrix,
            ControlPoints = controlPoints,
            Smoothness = smoothness
        };

        return true;
    }
}
