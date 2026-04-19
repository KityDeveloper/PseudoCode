# Notas de version

## 1.1.2 - 2026-04-19

Version estable enfocada en pulir el zoom del editor.

### Cambios principales

- `Ctrl + scroll` en el editor ahora se reserva para cambiar el tamano del texto y no mueve el scrollbar cuando llega al minimo o maximo.
- El tamano del texto del editor se guarda en `editor.fontSize` y se conserva al reiniciar la app.
- El zoom general de la interfaz con `Ctrl + +`, `Ctrl + -` y `Ctrl + 0` se guarda en `editor.interfaceScale`.
- Detener una ejecucion manualmente ya no aparece como diagnostico; solo se informa en la barra de estado.
- Version del proyecto actualizada a `1.1.2`.

## 1.1.1 - 2026-04-17

Version estable de documentacion y presentacion del producto.

### Cambios principales

- README actualizado con capturas actuales de la app.
- Screenshots renombrados con nombres descriptivos en `docs/assets/screenshots`.
- Galeria reorganizada para mostrar workspace vacio, editor, ejecucion viva, depuracion, diagnosticos, documentacion, configuracion JSON y Acerca de.
- Documentacion de publicacion actualizada para la version `1.1.1`.

### Notas

- Version estable, sin etiqueta beta ni alpha.
- Autor: Kity Dev.
- Algunos builds pueden no estar firmados todavia, por lo que Windows o macOS pueden mostrar una advertencia.

## 1.1.0 - 2026-04-17

Version estable enfocada en hacer la ejecucion mas parecida a una terminal real y mejorar programas interactivos tipo reloj/temporizador.

### Cambios principales

- Salida en vivo durante la ejecucion: la terminal se actualiza mientras el algoritmo corre, no solo al finalizar.
- `Borrar Pantalla` ahora limpia visualmente la salida durante la ejecucion.
- `Escribir` y `Esperar` publican actualizaciones intermedias para animaciones simples como relojes.
- Controles de ejecucion con `Play`, `Pausa/Continuar` y `Stop`.
- Cancelacion cooperativa del interprete para detener ejecuciones largas o pausadas.
- Proteccion contra ciclos/salidas enormes para evitar bloqueos de la app.
- Limpieza de diagnosticos, variables y subrayados viejos al iniciar una nueva ejecucion.
- Evaluacion de expresiones mas segura: no intenta reemplazar variables dentro de textos entre comillas.
- Mejor comportamiento para programas PSeInt con `Sin Saltar`, `Borrar Pantalla`, `Esperar` y `;`.
- GitHub Actions ahora genera paquetes macOS Intel (`osx-x64`) y Apple Silicon (`osx-arm64`).

### Notas

- Version estable, sin etiqueta beta ni alpha.
- Autor: Kity Dev.
- Algunos builds pueden no estar firmados todavia, por lo que Windows o macOS pueden mostrar una advertencia.

## 1.0.1 - 2026-04-17

Version estable de mantenimiento enfocada en pulir configuracion, documentacion y experiencia visual.

### Cambios principales

- Mejoras al editor de temas de sintaxis con panel de color mas claro, colores rapidos y vista previa del codigo.
- Vista previa de dialectos para ver como cambia el pseudocodigo al modificar palabras del lenguaje.
- Soporte PSeInt para `Borrar Pantalla`, `Esperar ... Segundos/Milisegundos` y `Escribir ... Sin Saltar`.
- Compatibilidad con `;` al final de instrucciones estilo PSeInt.
- Mejor contraste del colorizador JSON en modo claro.
- Subrayado de diagnostico visible dentro del preview de temas.
- Documentacion de pseudolenguaje ampliada con ejemplos de sintaxis y bloques de codigo.
- Links internos clickeables en la documentacion renderizada dentro de la app.
- Animaciones del autor en la ventana Acerca de usando los sprites de Kity Dev.
- Limpieza visual de la ventana Acerca de, sin boton inferior de cerrar.
- Ajustes de layout en configuracion para paneles mas legibles y redimensionables donde corresponde.

### Notas

- Version estable, sin etiqueta beta ni alpha.
- Autor: Kity Dev.
- Algunos builds pueden no estar firmados todavia, por lo que Windows o macOS pueden mostrar una advertencia.

## 1.0.0 - 2026-04-17

Primera version estable de PseudoCode.

### Cambios principales

- Version oficial estable, sin etiqueta beta ni alpha.
- Editor de pseudocodigo en español con dialecto principal estilo PSeInt.
- Ejecucion de algoritmos con salida, variables, entrada interactiva y estructuras `Si/Sino`, `Mientras`, `Para` y `Segun`.
- Depuracion paso a paso con `F10`, resaltado de la linea actual e inspeccion de variables.
- Diagnosticos en vivo con subrayado rojo, contador de problemas y salto a la linea del error.
- Tabs, multiples archivos, archivos recientes y cierre con confirmacion para guardar.
- Paneles redimensionables, modo claro/oscuro y zoom de interfaz/editor.
- Settings JSON para dialectos reemplazables y temas de sintaxis configurables.
- Editor visual de temas y dialectos desde la app, con aplicacion inmediata.
- Documentacion Markdown integrada desde la app.
- README de producto, guias de configuracion, desarrollo y publicacion.

### Notas

- Autor: Kity Dev.
- Algunos builds pueden no estar firmados todavia, por lo que Windows o macOS pueden mostrar una advertencia.
- macOS queda planeado como plataforma futura.

## 2.0.3-beta.1

### Cambios

- Refactor incremental de responsabilidades internas.
- Settings JSON para dialectos reemplazables.
- Colores de sintaxis configurables con tema principal `dark`.
- Documentacion Markdown renderizada dentro de la app.
- README orientado a usuarios y releases informativos.
- Version y autor visibles en la ventana Acerca de.

### Sobre PseudoCode

PseudoCode es una app educativa para escribir, ejecutar y aprender pseudocodigo en español con una experiencia inspirada en editores modernos.

### Autor

Creado por Kity Dev.
