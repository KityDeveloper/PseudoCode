# PseudoCode

PseudoCode es una app educativa de pseudocodigo en español hecha con .NET y Avalonia. La primera version apunta a una experiencia parecida a VS Code: editor central, barra lateral, panel de salida, ayuda rapida y ejecucion de algoritmos simples.

## Requisitos

- .NET SDK 8
- Linux, Windows o macOS para desarrollo


## Ejecutar

```bash
dotnet run --project src/PseudoCode.App
```

### Si ejecutas en un contenedor (Dev Container)

Para mostrar la interfaz gráfica de Avalonia en tu sistema anfitrión desde el contenedor:

1. En tu máquina anfitriona, permite conexiones locales a X11:
	```bash
	xhost +local:
	```
2. Dentro del contenedor, exporta la variable DISPLAY:
	```bash
	export DISPLAY=:0
	```
	(Si usas otra configuración, ajusta el valor de DISPLAY según corresponda.)
3. Ejecuta la app normalmente:
	```bash
	dotnet run --project src/PseudoCode.App
	```

Esto permitirá que la ventana de la app Avalonia se muestre en tu escritorio local.

## Dev containers

El proyecto incluye dos perfiles:

- `PseudoCode Avalonia`: entorno .NET 8 normal.
- `PseudoCode Avalonia - Bazzite`: entorno para Bazzite/Flatpak con montajes de X11/Wayland para probar la app Avalonia desde el contenedor.

## Publicar

### Release automatico con aprobacion

El workflow `Release` de GitHub Actions se ejecuta manualmente desde la pestana **Actions**. Pide un tag como `v1.0.0`, compila Windows y Linux, espera aprobacion en el environment `release`, y despues crea el tag y el GitHub Release con los paquetes:

- `PseudoCode-linux-x64-vX.Y.Z.tar.gz`
- `PseudoCode-win-x64-vX.Y.Z.zip`

Para que GitHub pida aprobacion antes de publicar, configura el environment en el repositorio:

1. Ve a **Settings > Environments**.
2. Crea un environment llamado `release`.
3. Activa **Required reviewers** y agregate como reviewer.

Linux x64:

```bash
dotnet publish src/PseudoCode.App -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/linux-x64
```

Windows x64:

```bash
dotnet publish src/PseudoCode.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/win-x64
```

## Pseudolenguaje inicial

La ejecucion actual soporta una base didactica:

- `Algoritmo`, `Proceso`, `FinAlgoritmo`, `FinProceso`
- `Definir nombre Como Entero`
- Asignaciones con `<-`
- `Escribir "texto", variable, 2 + 2`
- `Leer variable` con entrada simulada

El objetivo es crecer hacia un entorno estilo PSeInt con diagramas de flujo, validaciones, trazas paso a paso y mas estructuras del lenguaje.
