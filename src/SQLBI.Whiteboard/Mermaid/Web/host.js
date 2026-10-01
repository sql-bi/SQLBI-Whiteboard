/* Only the bundled renderer runs here. Diagram source is passed as data. */
"use strict";
mermaid.initialize({
  startOnLoad: false,
  securityLevel: "strict",
  suppressErrorRendering: true,
  htmlLabels: false,
  theme: "default",
  fontFamily: "Segoe UI",
  maxTextSize: 12000,
  maxEdges: 200,
  flowchart: { htmlLabels: false, useMaxWidth: false },
  sequence: { useMaxWidth: false },
  er: { useMaxWidth: false },
  mindmap: { useMaxWidth: false },
  state: { useMaxWidth: false },
  journey: { useMaxWidth: false },
});

// Some built-in renderers (notably event modeling) always use HTML labels.
// Keep the browser's line wrapping and inline styles, but retain only measured
// text, never HTML, links, or interactive content in the saved drawing.
function flattenHtmlLabels(root) {
  // Journey includes an SVG fallback after its HTML label. Chromium uses the
  // first branch; keep only that branch so the fallback is not drawn twice.
  for (const choice of [...root.querySelectorAll("switch")]) {
    if (choice.firstElementChild?.localName === "foreignObject")
      choice.replaceWith(choice.firstElementChild);
  }
  for (const foreign of [...root.querySelectorAll("foreignObject")]) {
    const group = document.createElementNS(root.namespaceURI, "g");
    foreign.before(group);
    const inverse = group.getScreenCTM().inverse();
    const walker = document.createTreeWalker(foreign, NodeFilter.SHOW_TEXT);
    for (let node = walker.nextNode(); node; node = walker.nextNode()) {
      const style = getComputedStyle(node.parentElement);
      if (style.visibility === "hidden" || style.display === "none") continue;
      let run = null;
      let previous = null;
      for (let index = 0; index < node.length;) {
        const character = String.fromCodePoint(node.nodeValue.codePointAt(index));
        const range = document.createRange();
        range.setStart(node, index);
        index += character.length;
        range.setEnd(node, index);
        const rect = range.getBoundingClientRect();
        if (!rect.width || !rect.height) continue;
        if (!run || Math.abs(rect.top - previous.top) > 1 || Math.abs(rect.left - previous.right) > 1) {
          run = document.createElementNS(root.namespaceURI, "text");
          for (const name of ["font-family", "font-size", "font-weight", "font-style"])
            run.style.setProperty(name, style.getPropertyValue(name), "important");
          run.style.setProperty("fill", style.color, "important");
          run.style.setProperty("text-anchor", "start", "important");
          run.style.setProperty("dominant-baseline", "alphabetic", "important");
          run.setAttributeNS("http://www.w3.org/XML/1998/namespace", "xml:space", "preserve");
          run.textContent = character;
          group.appendChild(run);
          // SVG and HTML character extents use the same font box in Chromium.
          const extent = run.getExtentOfChar(0);
          const point = new DOMPoint(rect.left, rect.top).matrixTransform(inverse);
          run.setAttribute("x", point.x - extent.x);
          run.setAttribute("y", point.y - extent.y);
        } else run.textContent += character;
        previous = rect;
      }
    }
    foreign.replaceWith(group);
  }
}

chrome.webview.addEventListener("message", async event => {
  const { id, source } = event.data;
  const target = document.getElementById("diagram");
  try {
    const { svg } = await mermaid.render("m" + id, source, target);
    target.innerHTML = svg;
    const root = target.querySelector("svg");
    if (!root) throw new Error("Missing diagram.");
    flattenHtmlLabels(root);
    const properties = [
      "fill", "fill-opacity", "stroke", "stroke-width", "stroke-opacity",
      "stroke-dasharray", "stroke-linecap", "stroke-linejoin", "opacity",
      "font-family", "font-size", "font-weight", "font-style", "text-anchor",
      "dominant-baseline", "visibility", "display"
    ];
    // Mermaid emits nested tspans and empty background rectangles. Flatten text
    // runs at the positions measured by the browser because SharpVectors does
    // not reproduce that nested text layout, and treats some empty rects as 100x100.
    root.querySelectorAll("rect").forEach(rect => {
      if (rect.width.baseVal.value <= 0 || rect.height.baseVal.value <= 0) rect.remove();
    });
    // Mermaid 12's SVG-only mind-map labels can be left-anchored even when
    // their node shape expects a centered label (notably circles).
    root.querySelectorAll(".mindmap-node > .label").forEach(label => {
      const box = label.getBBox();
      const transform = label.transform.baseVal.consolidate();
      if (transform && box.width > 0) transform.matrix.e = -box.x - box.width / 2;
    });
    for (const text of [...root.querySelectorAll("text")]) {
      if (!text.getNumberOfChars()) { text.remove(); continue; }
      const group = document.createElementNS(root.namespaceURI, "g");
      if (text.hasAttribute("transform")) group.setAttribute("transform", text.getAttribute("transform"));
      text.before(group);
      const walker = document.createTreeWalker(text, NodeFilter.SHOW_TEXT);
      let offset = 0;
      for (let node = walker.nextNode(); node; node = walker.nextNode()) {
        const value = node.nodeValue;
        if (!value) continue;
        const first = value.search(/\S/);
        if (first < 0) { offset += value.length; continue; }
        const run = document.createElementNS(root.namespaceURI, "text");
        const position = text.getStartPositionOfChar(offset + first);
        const style = getComputedStyle(node.parentElement);
        for (const name of properties) run.setAttribute(name, style.getPropertyValue(name));
        run.setAttribute("x", position.x);
        run.setAttribute("y", position.y);
        run.setAttribute("text-anchor", "start");
        run.setAttribute("dominant-baseline", "alphabetic");
        run.style.setProperty("text-anchor", "start", "important");
        run.style.setProperty("dominant-baseline", "alphabetic", "important");
        run.setAttributeNS("http://www.w3.org/XML/1998/namespace", "xml:space", "preserve");
        run.textContent = value.trim();
        group.appendChild(run);
        // getStartPositionOfChar does not account for hanging/middle baselines.
        // Compare character boxes while both layouts still exist in the DOM;
        // otherwise rotated chart-axis labels shift outside the viewport.
        const original = text.getExtentOfChar(offset + first);
        const flattened = run.getExtentOfChar(0);
        run.setAttribute("y", position.y + original.y - flattened.y);
        offset += value.length;
      }
      text.replaceWith(group);
    }
    const resolved = [...root.querySelectorAll("*")].map(element => {
      const style = getComputedStyle(element);
      return [element, properties.map(name => [name, style.getPropertyValue(name)])];
    });
    for (const [element, values] of resolved) {
      for (const [name, value] of values) if (value) element.setAttribute(name, value);
    }
    // The original stylesheet can override the presentation attribute while it
    // is still in the DOM. Flattened runs already have their left-edge position.
    root.querySelectorAll("text").forEach(text => text.setAttribute("text-anchor", "start"));
    root.querySelectorAll("style, script, a").forEach(element => {
      if (element.tagName.toLowerCase() === "a") {
        // Class diagrams position linked nodes on the anchor itself. Discard
        // the interaction, but preserve its transform and resolved appearance.
        const group = document.createElementNS(root.namespaceURI, "g");
        for (const name of ["id", "transform", "clip-path", ...properties])
          if (element.hasAttribute(name)) group.setAttribute(name, element.getAttribute(name));
        group.append(...element.childNodes);
        element.replaceWith(group);
      } else element.remove();
    });
    root.querySelectorAll("*").forEach(element => {
      for (const attribute of [...element.attributes]) {
        if (attribute.name.startsWith("on") || attribute.name === "style")
          element.removeAttribute(attribute.name);
      }
    });
    root.removeAttribute("style");
    const box = root.viewBox.baseVal;
    if (!(box.width > 0 && box.height > 0) || box.width > 20000 || box.height > 20000)
      throw new Error("Invalid diagram bounds.");
    root.setAttribute("width", box.width);
    root.setAttribute("height", box.height);
    // Some renderers draw connector stubs outside their declared viewBox.
    // Clip before decoding so those stubs cannot change DrawingImage's origin.
    const clipId = "viewport-" + id;
    const clip = document.createElementNS(root.namespaceURI, "clipPath");
    clip.id = clipId;
    const rectangle = document.createElementNS(root.namespaceURI, "rect");
    for (const name of ["x", "y", "width", "height"]) rectangle.setAttribute(name, box[name]);
    clip.appendChild(rectangle);
    const content = document.createElementNS(root.namespaceURI, "g");
    content.setAttribute("clip-path", "url(#" + clipId + ")");
    content.append(...root.childNodes);
    root.append(clip, content);
    chrome.webview.postMessage({ id, svg: new XMLSerializer().serializeToString(root) });
  } catch {
    chrome.webview.postMessage({ id, error: "Cannot render diagram" });
  } finally {
    target.replaceChildren();
  }
});
chrome.webview.postMessage({ id: "ready", svg: "" });
