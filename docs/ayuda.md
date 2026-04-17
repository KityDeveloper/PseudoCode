# Ayuda de PseudoCode

PseudoCode es una app educativa para escribir, ejecutar y aprender pseudocodigo en espanol. Esta documentacion vive en el repo como Markdown y tambien se puede abrir renderizada desde el menu **Ayuda** de la app.

## Indice

- [Primeros pasos](#primeros-pasos)
- [Flujo basico](#flujo-basico)
- [Editor](#editor)
- [Ejecucion](#ejecucion)
- [Configuracion](#configuracion)
- [Mas documentacion](#mas-documentacion)

## Primeros pasos

1. Abre la app.
2. Usa **Archivo > Nuevo algoritmo** o `Ctrl+N`.
3. Escribe un algoritmo con el dialecto activo. Por defecto es estilo PSeInt.
4. Usa **Ejecutar** para ver salida, diagnosticos y variables.

Ejemplo:

```text
Algoritmo Saludo
    Definir nombre Como Cadena
    nombre <- "Ada"
    Escribir "Hola ", nombre
FinAlgoritmo
```

## Flujo basico

- **Nuevo algoritmo** crea un archivo nuevo en blanco con estructura minima.
- **Abrir** permite cargar archivos `.psc`, `.pse` o `.txt`.
- **Guardar** guarda el archivo activo.
- **Cerrar archivo** pregunta si quieres guardar cuando hay cambios pendientes.

## Editor

El editor soporta tabs, multiples archivos, zoom del texto con `Ctrl` + scroll y zoom de interfaz con `Ctrl` + `+` o `Ctrl` + `-`.

## Ejecucion

La app puede ejecutar estructuras basicas de pseudocodigo: asignaciones, `Definir`, `Escribir`, `Leer`, `Si`, `Mientras`, `Para` y `Segun`.

## Depuracion paso a paso

Usa **Ejecutar > Iniciar depuracion** para comenzar una sesion paso a paso. La app selecciona la linea actual en el editor y muestra salida, diagnosticos y variables mientras avanzas.

- `F10`: ejecuta la linea actual y avanza al siguiente paso.
- **F10 Paso**: hace lo mismo desde el boton inferior.
- **Detener depuracion**: limpia la marca de linea actual.

Si editas el codigo, la sesion de depuracion se detiene para evitar que el paso actual quede desincronizado.

## Configuracion

El lenguaje activo y los colores de sintaxis se pueden cambiar con JSON. Lee [Configuracion JSON](configuracion-json.md) para ver rutas, ejemplos y reglas.

Desde la app tambien puedes usar el menu **Configuracion**:

- **Settings JSON** edita el archivo principal de settings.
- **Temas para el codigo fuente** edita temas de sintaxis.
- **Configurar sintaxis** edita dialectos.
- **Carpeta de configuracion** muestra rutas y crea plantillas JSON.

## Mas documentacion

- `docs/configuracion-json.md`
- `docs/pseudolenguaje.md`
- `docs/desarrollo.md`
- `docs/publicacion.md`
- `CHANGELOG.md`
