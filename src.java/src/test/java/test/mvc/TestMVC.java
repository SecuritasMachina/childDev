package test.mvc;

import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import org.junit.After;
import org.junit.Before;
import org.junit.Test;
import org.junit.runner.RunWith;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.autoconfigure.web.servlet.AutoConfigureMockMvc;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.http.MediaType;
import org.springframework.test.context.junit4.SpringRunner;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.test.web.servlet.MvcResult;

import com.ackdev.childDev.application.AppConfig;
import com.ackdev.childDev.jdo.GoalItem;
import com.ackdev.childDev.jdo.JournalItem;
import com.ackdev.common.dto.CommonResponse.Status;
import com.common.security.dto.RegisterUserDTO;
import com.common.security.jdo.UserAccountV2;
import com.google.gson.Gson;

import testUtils.DataStoreTestHelper;
import testUtils.Helpers;

@RunWith(SpringRunner.class)
@SpringBootTest(classes = AppConfig.class)
@AutoConfigureMockMvc
public class TestMVC {
	private Gson gson = new Gson();

	@Autowired
	private MockMvc mvc;

	@Before
	public void init() {
		DataStoreTestHelper.setupObjectify();

	}

	@After
	public void tearDown() {
		DataStoreTestHelper.closeObjectify();

	}

	@Test
	public void testUpdateJournalNoAuthFail() throws Exception {
		mvc.perform(post("/rest/secure/journal").contentType(MediaType.APPLICATION_JSON)
				.content(gson.toJson(Helpers.makeJournalItem())).accept(MediaType.APPLICATION_JSON))
				.andExpect(status().is(401));
		mvc.perform(post("/rest/secure/journal").contentType(MediaType.APPLICATION_JSON_UTF8_VALUE)
				.accept(MediaType.APPLICATION_JSON).content(gson.toJson(Helpers.makeJournalItem()))
				.header("azAuthHeader", "asdas")).andExpect(status().is(401));

	}

	@Test
	public void testUpdateGoalNoAuthFail() throws Exception {
		mvc.perform(post("/rest/secure/goal").contentType(MediaType.APPLICATION_JSON)
				.content(gson.toJson(Helpers.makeGoalItem())).accept(MediaType.APPLICATION_JSON))
				.andExpect(status().is(401));
		mvc.perform(post("/rest/secure/goal").contentType(MediaType.APPLICATION_JSON_UTF8_VALUE)
				.accept(MediaType.APPLICATION_JSON).content(gson.toJson(Helpers.makeGoalItem()))
				.header("azAuthHeader", "asdas")).andExpect(status().is(401));

	}

	@Test
	public void testUpdateJouralWithAuth() throws Exception {
		RegisterUserDTO userReg = Helpers.makeUserReg();
		MvcResult result = mvc
				.perform(post("/rest/account/register").contentType(MediaType.APPLICATION_JSON)
						.content(gson.toJson(userReg)).accept(MediaType.APPLICATION_JSON))
				.andExpect(status().isOk()).andReturn();
		UserAccountV2 ua = gson.fromJson(result.getResponse().getContentAsString(), UserAccountV2.class);

		assertTrue(ua.authHash.length() > 10);
		String path = "/rest/account/token/" + userReg.nickName + "/" + userReg.password;
		result = mvc.perform(get(path).accept(MediaType.APPLICATION_JSON)).andExpect(status().isOk()).andReturn();

		String contentAsString = result.getResponse().getContentAsString();
		assertTrue(contentAsString != null && contentAsString.length() > 10);
		ua = gson.fromJson(contentAsString, UserAccountV2.class);
		assertTrue(ua.authHash.length() > 10);

		result = mvc
				.perform(get("/rest/account/secure/getUser").accept(MediaType.APPLICATION_JSON).header("azAuthHeader", ua.authHash))
				.andExpect(status().isOk()).andReturn();
		ua = gson.fromJson(result.getResponse().getContentAsString(), UserAccountV2.class);
		assertTrue(ua != null);
		assertTrue(ua.authHash.length() > 10);
		JournalItem ji = Helpers.makeJournalItem();

		// spoof
		result = mvc.perform(post("/rest/secure/journal").contentType(MediaType.APPLICATION_JSON_UTF8_VALUE)
				.accept(MediaType.APPLICATION_JSON).content(gson.toJson(ji)).header("azAuthHeader", ua.authHash))
				.andExpect(status().isOk()).andReturn();
		Status s = gson.fromJson(result.getResponse().getContentAsString(), Status.class);
		assertFalse(s.isSuccess());

		ji.accountFk = ua.guid;
		result = mvc.perform(post("/rest/secure/journal").contentType(MediaType.APPLICATION_JSON_UTF8_VALUE)
				.accept(MediaType.APPLICATION_JSON).content(gson.toJson(ji)).header("azAuthHeader", ua.authHash))
				.andExpect(status().isOk()).andReturn();
		s = gson.fromJson(result.getResponse().getContentAsString(), Status.class);
		assertTrue(s.isSuccess());

	}

	@Test
	public void testUpdateGoalWithAuth() throws Exception {
		RegisterUserDTO userReg = Helpers.makeUserReg();
		mvc.perform(post("/rest/account/register").contentType(MediaType.APPLICATION_JSON).content(gson.toJson(userReg))
				.accept(MediaType.APPLICATION_JSON)).andExpect(status().isOk());
		String path = "/rest/account/token/" + userReg.nickName + "/" + userReg.password;
		MvcResult result = mvc.perform(get(path).accept(MediaType.APPLICATION_JSON)).andExpect(status().isOk())
				.andReturn();
		String contentAsString = result.getResponse().getContentAsString();
		assertTrue(contentAsString != null && contentAsString.length() > 10);
		UserAccountV2 ua = gson.fromJson(contentAsString, UserAccountV2.class);
		assertTrue(ua.authHash.length() > 10);

		GoalItem ji = Helpers.makeGoalItem();

		// spoof check
		result = mvc.perform(post("/rest/secure/goal").contentType(MediaType.APPLICATION_JSON_UTF8_VALUE)
				.accept(MediaType.APPLICATION_JSON).content(gson.toJson(ji)).header("azAuthHeader", ua.authHash))
				.andExpect(status().isOk()).andReturn();
		Status s = gson.fromJson(result.getResponse().getContentAsString(), Status.class);
		assertFalse(s.isSuccess());

		ji.accountFk = ua.guid;
		result = mvc.perform(post("/rest/secure/goal").contentType(MediaType.APPLICATION_JSON_UTF8_VALUE)
				.accept(MediaType.APPLICATION_JSON).content(gson.toJson(ji)).header("azAuthHeader", ua.authHash))
				.andExpect(status().isOk()).andReturn();
		s = gson.fromJson(result.getResponse().getContentAsString(), Status.class);
		assertTrue(s.isSuccess());

	}
}
