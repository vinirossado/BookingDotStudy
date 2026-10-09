namespace BookingDotStudy.Shared

/// Result of an idempotent domain operation, mapped to HTTP by the endpoints.
/// Mirrors Aurora's ProductUpdateResult.
type UpdateResult =
    | Updated
    | NoChanges
    | NotFound
