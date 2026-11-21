// Eleventy is used only to stamp the shared header/footer layout into plain HTML pages.
export default function (eleventyConfig) {
  eleventyConfig.addPassthroughCopy({ "src/css": "css", "src/js": "js", "src/img": "img" });
  eleventyConfig.addPassthroughCopy({ "src/staticwebapp.config.json": "staticwebapp.config.json" });
  eleventyConfig.addPassthroughCopy({ "src/robots.txt": "robots.txt" });

  eleventyConfig.addFilter("year", () => new Date().getFullYear());

  return {
    dir: { input: "src", includes: "_includes", data: "_data", output: "_site" },
    htmlTemplateEngine: "njk",
    templateFormats: ["html", "njk", "md"],
  };
}
