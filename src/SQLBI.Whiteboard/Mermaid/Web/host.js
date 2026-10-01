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
});

chrome.webview.addEventListener("message", async event => {
  const { id, source } = event.data;
  const target = document.getElementById("diagram");
  try {
    const { svg } = await mermaid.render("m" + id, source, target);
    target.innerHTML = svg;
    const root = target.querySelector("svg");
    if (!root || root.querySelector("foreignObject"))
      throw new Error("This diagram requires HTML labels.");
    const properties = [
      "fill", "fill-opacity", "stroke", "stroke-width", "stroke-opacity",
      "stroke-dasharray", "stroke-linecap", "stroke-linejoin", "opacity",
      "font-family", "font-size", "font-weight", "font-style", "text-anchor",
      "dominant-baseline", "visibility"
    ];
    // Mermaid emits nested tspans and empty background rectangles. Flatten text
    // runs at the positions measured by the browser because SharpVectors does
    // not reproduce that nested text layout, and treats some empty rects as 100x100.
    root.querySelectorAll("rect").forEach(rect => {
      if (rect.width.baseVal.value <= 0 || rect.height.baseVal.value <= 0) rect.remove();
    });
    for (const text of [...root.querySelectorAll("text")]) {
      const group = document.createElementNS(root.namespaceURI, "g");
      if (text.hasAttribute("transform")) group.setAttribute("transform", text.getAttribute("transform"));
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
        run.setAttributeNS("http://www.w3.org/XML/1998/namespace", "xml:space", "preserve");
        run.textContent = value.trim();
        group.appendChild(run);
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
      if (element.tagName.toLowerCase() === "a") element.replaceWith(...element.childNodes);
      else element.remove();
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
    chrome.webview.postMessage({ id, svg: new XMLSerializer().serializeToString(root) });
  } catch {
    chrome.webview.postMessage({ id, error: "Cannot render diagram" });
  } finally {
    target.replaceChildren();
  }
});
chrome.webview.postMessage({ id: "ready", svg: "" });
