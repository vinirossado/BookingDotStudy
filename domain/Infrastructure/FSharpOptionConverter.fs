namespace BookingDotStudy.Domain.Infrastructure

open System
open Newtonsoft.Json
open Microsoft.FSharp.Reflection

/// Serializes F# `'T option` transparently for Marten/Newtonsoft.
///
/// Without this, Json.NET's built-in DU converter would persist `Some v`
/// as `{"Case":"Some","Fields":[v]}`, which is ugly and brittle. This
/// converter writes `None` as JSON null and `Some v` as the bare value,
/// and on read accepts both the bare shape AND the legacy wrapper shape
/// (so a field can be widened from `'T` to `'T option` without a migration).
type FSharpOptionConverter() =
    inherit JsonConverter()

    override _.CanConvert(t: Type) =
        t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<option<_>>

    override _.WriteJson(writer: JsonWriter, value: obj, serializer: JsonSerializer) =
        match value with
        | null -> writer.WriteNull()
        | some ->
            let _, fields = FSharpValue.GetUnionFields(some, some.GetType())
            serializer.Serialize(writer, fields.[0])

    override _.ReadJson(reader: JsonReader, objectType: Type, _existing: obj, serializer: JsonSerializer) =
        let innerType = objectType.GetGenericArguments().[0]
        let cases = FSharpType.GetUnionCases(objectType)
        let noneCase = cases |> Array.find (fun c -> c.Name = "None")
        let someCase = cases |> Array.find (fun c -> c.Name = "Some")

        let makeSome (value: obj) =
            if isNull value then
                FSharpValue.MakeUnion(noneCase, [||])
            else
                FSharpValue.MakeUnion(someCase, [| value |])

        match reader.TokenType with
        | JsonToken.Null -> FSharpValue.MakeUnion(noneCase, [||])
        | JsonToken.StartObject ->
            // A JSON object here is almost always `Some record` written in the
            // bare shape (e.g. Loan option -> the Loan object). Only unwrap the
            // LEGACY Json.NET DU shape {"Case":"Some","Fields":[value]} when BOTH
            // "Case" and "Fields" are present; otherwise read the object as the
            // bare inner value. (Guarding on "Fields" alone would wrongly treat a
            // record that happens to have a Fields property as a wrapper.)
            let jObj = Newtonsoft.Json.Linq.JObject.Load(reader)
            let isLegacyWrapper = not (isNull jObj.["Case"]) && not (isNull jObj.["Fields"])

            if isLegacyWrapper then
                let fields = jObj.["Fields"]
                makeSome (fields.[0].ToObject(innerType, serializer))
            else
                makeSome (jObj.ToObject(innerType, serializer))
        | _ ->
            // Bare scalar/array value shape.
            makeSome (serializer.Deserialize(reader, innerType))
