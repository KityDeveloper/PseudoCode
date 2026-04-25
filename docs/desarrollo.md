# Desarrollo

## Requisitos

- .NET SDK 10
- Linux, Windows o macOS para desarrollo

## Ejecutar

```bash
dotnet run --project src/PseudoCode.App
```

## Si ejecutas en un contenedor

Para mostrar la interfaz grafica de Avalonia en tu sistema anfitrion desde el contenedor:

1. En tu maquina anfitriona, permite conexiones locales a X11:

```bash
xhost +local:
```

2. Dentro del contenedor, exporta la variable `DISPLAY`:

```bash
export DISPLAY=:0
```

3. Ejecuta la app:

```bash
dotnet run --project src/PseudoCode.App
```

### Dependencias nativas de Avalonia

En Debian, Ubuntu o devcontainers basados en estas distribuciones, Avalonia necesita algunas librerias nativas de X11. Los devcontainers del repo ya las instalan, incluyendo `libxcursor1`.

Si ves un error como `Unable to load shared library 'libXcursor.so.1'` al arrastrar texto o mover el cursor durante drag and drop, instala:

```bash
sudo apt-get update
sudo apt-get install -y libxcursor1
```

En Fedora/Bazzite, el paquete equivalente es:

```bash
sudo dnf install -y libXcursor
```

## Dev containers

El proyecto incluye dos perfiles:

- `PseudoCode Avalonia`: entorno .NET 10 normal.
- `PseudoCode Avalonia - Bazzite`: entorno para Bazzite/Flatpak con montajes de X11/Wayland.
