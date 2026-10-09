namespace BookingDotStudy.Domain

open Microsoft.Extensions.Configuration

/// Resolves the Postgres connection string from (in order):
///   1. POSTGRES_CONNECTION_STRING environment variable / config key
///   2. ConnectionStrings:DefaultConnection
///   3. a localhost default matching docker-compose.yml
type PostgresConfig = { ConnectionString: string }

module PostgresConfig =

    [<Literal>]
    let private DefaultConnection =
        "Host=localhost;Port=5433;Database=booking_study;Username=booking;Password=booking;"

    let fromConfiguration (configuration: IConfiguration) : PostgresConfig =
        let fromEnv = configuration.["POSTGRES_CONNECTION_STRING"]
        let fromConnStrings = configuration.GetConnectionString("DefaultConnection")

        let connectionString =
            [ fromEnv; fromConnStrings ]
            |> List.tryFind (fun v -> not (System.String.IsNullOrWhiteSpace v))
            |> Option.defaultValue DefaultConnection

        { ConnectionString = connectionString }
