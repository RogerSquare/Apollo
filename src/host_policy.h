/**
 * @file src/host_policy.h
 * @brief Declarations for the per-device host policy (what the host does to its own
 *        displays and audio while a client is streaming).
 */
#pragma once

// standard includes
#include <optional>
#include <string>
#include <string_view>

namespace host_policy {

  /**
   * @brief What to do with the host's physical displays while streaming to a virtual display.
   */
  enum class display_e {
    stock,  ///< Leave display arrangement to Apollo's existing behavior.
    primary,  ///< Virtual display becomes primary, physical displays stay extended.
    exclusive,  ///< Virtual display becomes primary, physical displays are detached.
  };

  /**
   * @brief Fully resolved policy applied to a session.
   */
  struct settings_t {
    display_e display = display_e::stock;
    bool mute_wavelink = false;

    bool operator==(const settings_t &) const = default;
  };

  /**
   * @brief Per-client override; an empty field inherits the global default.
   */
  struct override_t {
    std::optional<display_e> display;
    std::optional<bool> mute_wavelink;

    bool operator==(const override_t &) const = default;
  };

  /**
   * @brief Resolve a client's effective policy from the global default and its override.
   */
  settings_t resolve(const settings_t &global, const override_t &client);

  std::string_view to_string(display_e display);
  std::optional<display_e> display_from_string(std::string_view value);

  /**
   * @brief Override field <-> string. "inherit" (or empty) maps to no override.
   */
  std::string override_display_to_string(const std::optional<display_e> &display);
  std::optional<display_e> override_display_from_string(std::string_view value);
  std::string override_bool_to_string(const std::optional<bool> &value);
  std::optional<bool> override_bool_from_string(std::string_view value);

}  // namespace host_policy
