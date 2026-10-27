/**
 * @file tests/unit/test_host_policy.cpp
 * @brief Test src/host_policy.*.
 */
#include "../tests_common.h"

#include <src/host_policy.h>

using host_policy::display_e;
using host_policy::override_t;
using host_policy::settings_t;

TEST(HostPolicyResolve, EmptyOverrideInheritsGlobal) {
  const settings_t global {display_e::exclusive, true};
  EXPECT_EQ(host_policy::resolve(global, {}), global);
}

TEST(HostPolicyResolve, ClientOverridesEachFieldIndependently) {
  const settings_t global {display_e::exclusive, true};

  EXPECT_EQ(host_policy::resolve(global, override_t {display_e::primary, std::nullopt}), (settings_t {display_e::primary, true}));
  EXPECT_EQ(host_policy::resolve(global, override_t {std::nullopt, false}), (settings_t {display_e::exclusive, false}));
  EXPECT_EQ(host_policy::resolve(global, override_t {display_e::stock, false}), (settings_t {display_e::stock, false}));
}

TEST(HostPolicyResolve, DefaultsMatchStockApollo) {
  EXPECT_EQ(host_policy::resolve({}, {}), (settings_t {display_e::stock, false}));
}

TEST(HostPolicyStrings, DisplayRoundTrips) {
  for (auto d : {display_e::stock, display_e::primary, display_e::exclusive}) {
    EXPECT_EQ(host_policy::display_from_string(host_policy::to_string(d)), d);
    EXPECT_EQ(host_policy::override_display_from_string(host_policy::override_display_to_string(d)), d);
  }
}

TEST(HostPolicyStrings, InheritAndUnknownMapToNoOverride) {
  EXPECT_EQ(host_policy::override_display_to_string(std::nullopt), "inherit");
  EXPECT_EQ(host_policy::override_display_from_string("inherit"), std::nullopt);
  EXPECT_EQ(host_policy::override_display_from_string(""), std::nullopt);
  EXPECT_EQ(host_policy::override_display_from_string("bogus"), std::nullopt);
  EXPECT_EQ(host_policy::display_from_string("bogus"), std::nullopt);
}

TEST(HostPolicyStrings, BoolOverride) {
  EXPECT_EQ(host_policy::override_bool_to_string(std::nullopt), "inherit");
  EXPECT_EQ(host_policy::override_bool_to_string(true), "on");
  EXPECT_EQ(host_policy::override_bool_to_string(false), "off");
  EXPECT_EQ(host_policy::override_bool_from_string("on"), true);
  EXPECT_EQ(host_policy::override_bool_from_string("off"), false);
  EXPECT_EQ(host_policy::override_bool_from_string("inherit"), std::nullopt);
  EXPECT_EQ(host_policy::override_bool_from_string("maybe"), std::nullopt);
}
