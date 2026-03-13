# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Cloud Image Uploader.csproj", "./"]
RUN dotnet restore "Cloud Image Uploader.csproj"

COPY . .
RUN dotnet publish "Cloud Image Uploader.csproj" -c Release -o /app/publish

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Cloud Image Uploader.dll"]
