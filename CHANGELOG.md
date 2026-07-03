# Changelog

## [1.0.1](https://github.com/DevSecNinja/az-vm-start-stop/compare/v1.0.0...v1.0.1) (2026-07-03)


### Miscellaneous Chores

* release 1.0.1 ([fc4313d](https://github.com/DevSecNinja/az-vm-start-stop/commit/fc4313d7eb418bdcea272a73fd178ac3d1650751))

## [1.0.0](https://github.com/DevSecNinja/az-vm-start-stop/compare/v0.1.0...v1.0.0) (2026-07-03)


### ⚠ BREAKING CHANGES

* pin Azure Functions Worker to v1 (v2 broke runtime on Flex)

### Features

* add near-real-time liveness alert for the function ([d60d9c9](https://github.com/DevSecNinja/az-vm-start-stop/commit/d60d9c9bda3e35efb75facd9866594fac9516015))
* add tag-driven auto-stop (AutoStop) and rename to az-vm-start-stop ([3b123a1](https://github.com/DevSecNinja/az-vm-start-stop/commit/3b123a1daf9db4429875582690d125bbb3b73af8))
* add tag-driven auto-stop and rename to az-vm-start-stop ([4467edd](https://github.com/DevSecNinja/az-vm-start-stop/commit/4467edda0a28ea47133d14e6842a1ad1746cc532))
* confirm start/stop completion with timeout, add tests and docs ([0acff39](https://github.com/DevSecNinja/az-vm-start-stop/commit/0acff39c38922d13b2331ff7a179e69040205831))
* **deps:** update azure azure-sdk-for-net monorepo [automerge] ([#8](https://github.com/DevSecNinja/az-vm-start-stop/issues/8)) ([39575b6](https://github.com/DevSecNinja/az-vm-start-stop/commit/39575b65771c4b302213f78268e7b85c961af6b8))
* **deps:** Update azure-functions-dotnet-worker monorepo ([#14](https://github.com/DevSecNinja/az-vm-start-stop/issues/14)) ([3c02ff8](https://github.com/DevSecNinja/az-vm-start-stop/commit/3c02ff8557e4bfcb0e6fa0a991c75ebbb94b3cc9))
* **deps:** update azure-functions-dotnet-worker monorepo [automerge] ([#9](https://github.com/DevSecNinja/az-vm-start-stop/issues/9)) ([a33230d](https://github.com/DevSecNinja/az-vm-start-stop/commit/a33230d2c9c8d6c48c70b5d0b9230d614199e6fd))
* **deps:** update dependency microsoft.net.test.sdk ( 17.11.1 ➔ 17.14.1 ) [automerge] ([#10](https://github.com/DevSecNinja/az-vm-start-stop/issues/10)) ([c352a0e](https://github.com/DevSecNinja/az-vm-start-stop/commit/c352a0e12d9b53305cff8bd2c16a2ce42a5000d5))
* **deps:** Update dependency Microsoft.NET.Test.Sdk ( 17.14.1 ➔ 18.6.0 ) ([#16](https://github.com/DevSecNinja/az-vm-start-stop/issues/16)) ([14c8565](https://github.com/DevSecNinja/az-vm-start-stop/commit/14c8565ba956fad48319d2f15fff2c5035585874))
* **deps:** update dependency microsoft.net.test.sdk ( 18.6.0 ➔ 18.7.0 ) [automerge] ([#21](https://github.com/DevSecNinja/az-vm-start-stop/issues/21)) ([ec006a2](https://github.com/DevSecNinja/az-vm-start-stop/commit/ec006a23986410494f1aae56cd396b903635caf1))
* **deps:** update dependency ncrontab.signed ( 3.3.3 ➔ 3.4.0 ) [automerge] ([#11](https://github.com/DevSecNinja/az-vm-start-stop/issues/11)) ([371aea2](https://github.com/DevSecNinja/az-vm-start-stop/commit/371aea2a966007e6cad65cbf3556ecdbac8fe348))
* **deps:** Update dependency xunit.runner.visualstudio ( 2.8.2 ➔ 3.1.5 ) ([#17](https://github.com/DevSecNinja/az-vm-start-stop/issues/17)) ([0682235](https://github.com/DevSecNinja/az-vm-start-stop/commit/068223514b118d0a148c206e5d05c2ed322e7deb))
* **deps:** Update dotnet monorepo ( 8.0.3 ➔ 10.0.9 ) ([#18](https://github.com/DevSecNinja/az-vm-start-stop/issues/18)) ([e20eef2](https://github.com/DevSecNinja/az-vm-start-stop/commit/e20eef21207ccde2c9a7924a71a2658f0cb109b4))
* **deps:** update Microsoft.ApplicationInsights.WorkerService to 3.1.2 ([#15](https://github.com/DevSecNinja/az-vm-start-stop/issues/15)) ([9df7051](https://github.com/DevSecNinja/az-vm-start-stop/commit/9df7051537246319725585cd0f6745eb30603e1a))
* log every scan step, assumption and error for schedule diagnosis ([92b7d16](https://github.com/DevSecNinja/az-vm-start-stop/commit/92b7d16e60334d7202c155aa0d7cde9031e60782)), closes [#5](https://github.com/DevSecNinja/az-vm-start-stop/issues/5)
* stamp commit SHA into function logs for version traceability ([28b35b5](https://github.com/DevSecNinja/az-vm-start-stop/commit/28b35b5c549ad1089f16d95aa34d3fd943b80454))
* tag-driven Azure VM auto-start Function (.NET 8 isolated) ([975c85c](https://github.com/DevSecNinja/az-vm-start-stop/commit/975c85c353408b64e24f2666d7536d4ac7ee3038))


### Bug Fixes

* **ci:** checkout repo in nightly check so it can run the extracted script ([ea91e24](https://github.com/DevSecNinja/az-vm-start-stop/commit/ea91e24f7b63c73545a6701d649ba7390b0cc82d))
* **deps:** update dependency microsoft.extensions.logging.abstractions ( 8.0.2 ➔ 8.0.3 ) [automerge] ([ed34900](https://github.com/DevSecNinja/az-vm-start-stop/commit/ed3490047e9e23c2d1f4af05bdff5f7698fb361b))
* **deps:** update dependency microsoft.extensions.logging.abstractions ( 8.0.2 ➔ 8.0.3 ) [automerge] ([eac8c17](https://github.com/DevSecNinja/az-vm-start-stop/commit/eac8c17cdb0d4d2b090fd64e933d0a46258497f9))
* **deps:** update dependency xunit ( 2.9.2 ➔ 2.9.3 ) [automerge] ([6fed06a](https://github.com/DevSecNinja/az-vm-start-stop/commit/6fed06a4c40dde81da8193db6cccdf710b5d5189))
* **deps:** update dependency xunit ( 2.9.2 ➔ 2.9.3 ) [automerge] ([1f0c242](https://github.com/DevSecNinja/az-vm-start-stop/commit/1f0c24295dbec90496d349778e6490c53d000095))
* **devcontainer:** install Functions Core Tools from Microsoft's official feed ([9c470ca](https://github.com/DevSecNinja/az-vm-start-stop/commit/9c470cae9bc52220922caa05ba5b7706ac07914a))
* **devcontainer:** use a valid Azure Functions Core Tools feature ([5709502](https://github.com/DevSecNinja/az-vm-start-stop/commit/570950295483477a8f63f3b1cafb2b521f6058fc))
* **infra:** CAF-compliant resource names via azd abbreviations.json ([2fae758](https://github.com/DevSecNinja/az-vm-start-stop/commit/2fae7583918d2850b03f74f97d39ac12b5b4543c))
* **infra:** use __ in app-setting names (Azure rejects ':') ([e22bf78](https://github.com/DevSecNinja/az-vm-start-stop/commit/e22bf78cd1df9bbbd30cb952fd7f279af8bc94b3))
* pin App Insights WorkerService to 2.x; restore Worker v2 ([9990f35](https://github.com/DevSecNinja/az-vm-start-stop/commit/9990f35b239d96ec650aee2070672a3f341932cd))
* register App Insights log-filter removal after AI setup so it wins ([cfaef5b](https://github.com/DevSecNinja/az-vm-start-stop/commit/cfaef5b662499445216c7335f7be0105003f69ff)), closes [#5](https://github.com/DevSecNinja/az-vm-start-stop/issues/5)
* scan all accessible subscriptions, not just the default one ([1998050](https://github.com/DevSecNinja/az-vm-start-stop/commit/19980503061146555ac684d360dda269ff43d3c1)), closes [#5](https://github.com/DevSecNinja/az-vm-start-stop/issues/5)
* surface Information logs and add structured diagnostics for schedule pass ([783f446](https://github.com/DevSecNinja/az-vm-start-stop/commit/783f4464b0c9059b93a69b20c8bc49af5010043b)), closes [#5](https://github.com/DevSecNinja/az-vm-start-stop/issues/5)


### Reverts

* back to Worker v1 to restore service after v2 crash repro ([e1c9b0c](https://github.com/DevSecNinja/az-vm-start-stop/commit/e1c9b0ca4c1753b6bf97e1bc57a15d322734c1b5))
* pin Azure Functions Worker to v1 (v2 broke runtime on Flex) ([9740181](https://github.com/DevSecNinja/az-vm-start-stop/commit/9740181e79dbb00a5ee85564c4cdc0badda3ac43))
